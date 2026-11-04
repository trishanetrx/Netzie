using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;
using NetSentry.Models;

namespace NetSentry.Services
{
    public class NetworkMonitorService : IDisposable
    {
        private readonly DispatcherTimer _refreshTimer;
        private Thread? _etwThread;
        private TraceEventSession? _etwSession;
        private bool _isDisposed;

        // ETW delta counters
        private readonly ConcurrentDictionary<int, ProcessByteDelta> _etwDeltas = new();
        // Win32 Process IO trackers
        private readonly ConcurrentDictionary<int, (ulong readBytes, ulong writeBytes, DateTime timestamp)> _ioHistory = new();
        // Process detail cache
        private readonly ConcurrentDictionary<int, CachedProcessDetails> _processCache = new();
        // Consecutive seconds above threshold tracker
        private readonly ConcurrentDictionary<int, int> _consecutiveSecondsAboveThreshold = new();
        public int SustainedBreachSecondsRequired { get; set; } = 5;

        private long _lastTotalBytesReceived;
        private long _lastTotalBytesSent;
        private DateTime _lastSampleTime = DateTime.UtcNow;

        public double TotalDownloadBytesPerSec { get; private set; }
        public double TotalUploadBytesPerSec { get; private set; }
        public double ThresholdMbps { get; set; } = 1.0;

        public long SessionDownloadedBytes { get; private set; }
        public long SessionUploadedBytes { get; private set; }
        public long SessionTotalBytes => SessionDownloadedBytes + SessionUploadedBytes;
        public DateTime SessionStartTime { get; private set; } = DateTime.UtcNow;

        public void ResetSessionStats()
        {
            SessionDownloadedBytes = 0;
            SessionUploadedBytes = 0;
            SessionStartTime = DateTime.UtcNow;
        }

        public event Action<double, double, long, long>? TotalSpeedUpdated;
        public event Action<List<ProcessNetworkInfo>>? ProcessListUpdated;
        public event Action<ProcessNetworkInfo>? ThresholdBreached;

        #region Win32 P/Invoke Definitions

        private const int AF_INET = 2;
        private const int AF_INET6 = 23;
        private const int TCP_TABLE_OWNER_PID_ALL = 5;

        [DllImport("iphlpapi.dll", SetLastError = true)]
        private static extern uint GetExtendedTcpTable(
            IntPtr pTcpTable,
            ref int pdwSize,
            bool bOrder,
            int ulAf,
            int TableClass,
            uint Reserved = 0);

        [StructLayout(LayoutKind.Sequential)]
        public struct IO_COUNTERS
        {
            public ulong ReadOperationCount;
            public ulong WriteOperationCount;
            public ulong OtherOperationCount;
            public ulong ReadTransferCount;
            public ulong WriteTransferCount;
            public ulong OtherTransferCount;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GetProcessIoCounters(IntPtr hProcess, out IO_COUNTERS lpIoCounters);

        #endregion

        private class ProcessByteDelta
        {
            public long RxBytes;
            public long TxBytes;
        }

        private class CachedProcessDetails
        {
            public string Name = string.Empty;
            public string ExePath = string.Empty;
            public BitmapSource? Icon;
        }

        public NetworkMonitorService()
        {
            InitTotalCounters();

            _refreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _refreshTimer.Tick += OnTick;
        }

        public void Start()
        {
            StartEtwSession();
            _refreshTimer.Start();
        }

        public void Stop()
        {
            _refreshTimer.Stop();
            StopEtwSession();
        }

        private void InitTotalCounters()
        {
            long rx = 0, tx = 0;
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus == OperationalStatus.Up && ni.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                {
                    try
                    {
                        var stats = ni.GetIPStatistics();
                        rx += stats.BytesReceived;
                        tx += stats.BytesSent;
                    }
                    catch { }
                }
            }
            _lastTotalBytesReceived = rx;
            _lastTotalBytesSent = tx;
            _lastSampleTime = DateTime.UtcNow;
        }

        private void StartEtwSession()
        {
            _etwThread = new Thread(() =>
            {
                try
                {
                    string sessionName = "NetSentryKernelSession_" + Guid.NewGuid().ToString("N").Substring(0, 8);
                    _etwSession = new TraceEventSession(sessionName);
                    _etwSession.EnableKernelProvider(KernelTraceEventParser.Keywords.NetworkTCPIP);

                    _etwSession.Source.Kernel.TcpIpRecv += data => RecordEtwBytes(data.ProcessID, data.size, 0);
                    _etwSession.Source.Kernel.TcpIpSend += data => RecordEtwBytes(data.ProcessID, 0, data.size);
                    _etwSession.Source.Kernel.TcpIpRecvIPV6 += data => RecordEtwBytes(data.ProcessID, data.size, 0);
                    _etwSession.Source.Kernel.TcpIpSendIPV6 += data => RecordEtwBytes(data.ProcessID, 0, data.size);
                    _etwSession.Source.Kernel.UdpIpRecv += data => RecordEtwBytes(data.ProcessID, data.size, 0);
                    _etwSession.Source.Kernel.UdpIpSend += data => RecordEtwBytes(data.ProcessID, 0, data.size);
                    _etwSession.Source.Kernel.UdpIpRecvIPV6 += data => RecordEtwBytes(data.ProcessID, data.size, 0);
                    _etwSession.Source.Kernel.UdpIpSendIPV6 += data => RecordEtwBytes(data.ProcessID, 0, data.size);

                    _etwSession.Source.Process();
                }
                catch
                {
                    // If running without elevation, ETW will fail gracefully; Win32 IO counters take over!
                }
            })
            {
                IsBackground = true,
                Priority = ThreadPriority.AboveNormal
            };

            _etwThread.Start();
        }

        private void RecordEtwBytes(int pid, int rx, int tx)
        {
            if (pid <= 0) return;
            var delta = _etwDeltas.GetOrAdd(pid, _ => new ProcessByteDelta());
            if (rx > 0) Interlocked.Add(ref delta.RxBytes, rx);
            if (tx > 0) Interlocked.Add(ref delta.TxBytes, tx);
        }

        private void StopEtwSession()
        {
            try
            {
                if (_etwSession != null)
                {
                    _etwSession.Dispose();
                    _etwSession = null;
                }
            }
            catch { }
        }

        private void OnTick(object? sender, EventArgs e)
        {
            DateTime now = DateTime.UtcNow;
            double elapsedSec = (now - _lastSampleTime).TotalSeconds;
            if (elapsedSec <= 0) elapsedSec = 1.0;

            // 1. Adapter Total Speed
            long currentRx = 0, currentTx = 0;
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus == OperationalStatus.Up && ni.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                {
                    try
                    {
                        var stats = ni.GetIPStatistics();
                        currentRx += stats.BytesReceived;
                        currentTx += stats.BytesSent;
                    }
                    catch { }
                }
            }

            long deltaRx = Math.Max(0, currentRx - _lastTotalBytesReceived);
            long deltaTx = Math.Max(0, currentTx - _lastTotalBytesSent);

            TotalDownloadBytesPerSec = deltaRx / elapsedSec;
            TotalUploadBytesPerSec = deltaTx / elapsedSec;

            SessionDownloadedBytes += deltaRx;
            SessionUploadedBytes += deltaTx;

            _lastTotalBytesReceived = currentRx;
            _lastTotalBytesSent = currentTx;
            _lastSampleTime = now;

            TotalSpeedUpdated?.Invoke(TotalDownloadBytesPerSec, TotalUploadBytesPerSec, SessionDownloadedBytes, SessionUploadedBytes);

            // 2. Discover ALL Processes with Active TCP Connections (Zero admin required)
            var activePids = GetActiveTcpProcessIds();

            // Also include any PIDs that had ETW packet hits
            foreach (var key in _etwDeltas.Keys)
            {
                activePids.Add(key);
            }

            var processList = new List<ProcessNetworkInfo>();

            foreach (int pid in activePids)
            {
                if (pid <= 4) continue; // Skip System idle/kernel

                double rxRate = 0;
                double txRate = 0;

                // Priority A: ETW packets if captured
                if (_etwDeltas.TryGetValue(pid, out var etwDelta))
                {
                    long eRx = Interlocked.Exchange(ref etwDelta.RxBytes, 0);
                    long eTx = Interlocked.Exchange(ref etwDelta.TxBytes, 0);
                    if (eRx > 0 || eTx > 0)
                    {
                        rxRate = eRx / elapsedSec;
                        txRate = eTx / elapsedSec;
                    }
                }

                // Priority B: Process IO Counters (delta read/write)
                try
                {
                    using var proc = Process.GetProcessById(pid);
                    if (GetProcessIoCounters(proc.Handle, out var io))
                    {
                        if (_ioHistory.TryGetValue(pid, out var hist))
                        {
                            double ioElapsed = (now - hist.timestamp).TotalSeconds;
                            if (ioElapsed > 0)
                            {
                                ulong dRead = io.ReadTransferCount >= hist.readBytes ? io.ReadTransferCount - hist.readBytes : 0;
                                ulong dWrite = io.WriteTransferCount >= hist.writeBytes ? io.WriteTransferCount - hist.writeBytes : 0;

                                if (rxRate == 0) rxRate = dRead / ioElapsed;
                                if (txRate == 0) txRate = dWrite / ioElapsed;
                            }
                        }

                        _ioHistory[pid] = (io.ReadTransferCount, io.WriteTransferCount, now);
                    }
                }
                catch { }

                var details = GetProcessDetails(pid);
                if (details == null) continue;

                double totalMbps = ((rxRate + txRate) * 8.0) / 1_000_000.0;
                bool aboveThreshold = totalMbps >= ThresholdMbps;

                var info = new ProcessNetworkInfo
                {
                    ProcessId = pid,
                    ProcessName = details.Name,
                    ExecutablePath = details.ExePath,
                    Icon = details.Icon,
                    DownloadSpeedBytes = rxRate,
                    UploadSpeedBytes = txRate,
                    TotalSpeedMbps = totalMbps,
                    IsAboveThreshold = aboveThreshold
                };

                processList.Add(info);

                if (aboveThreshold)
                {
                    int consecutive = _consecutiveSecondsAboveThreshold.AddOrUpdate(pid, 1, (_, count) => count + 1);
                    if (consecutive >= SustainedBreachSecondsRequired)
                    {
                        ThresholdBreached?.Invoke(info);
                    }
                }
                else
                {
                    _consecutiveSecondsAboveThreshold.TryRemove(pid, out _);
                }
            }

            // Clean up stale IO history and breach trackers
            var currentPidSet = new HashSet<int>(activePids);
            foreach (var deadPid in _ioHistory.Keys.Where(k => !currentPidSet.Contains(k)).ToList())
            {
                _ioHistory.TryRemove(deadPid, out _);
            }
            foreach (var deadPid in _consecutiveSecondsAboveThreshold.Keys.Where(k => !currentPidSet.Contains(k)).ToList())
            {
                _consecutiveSecondsAboveThreshold.TryRemove(deadPid, out _);
            }

            // Sort: highest bandwidth first, then by name
            var sorted = processList
                .OrderByDescending(p => p.TotalSpeedMbps)
                .ThenBy(p => p.ProcessName)
                .ToList();

            ProcessListUpdated?.Invoke(sorted);
        }

        private HashSet<int> GetActiveTcpProcessIds()
        {
            var pids = new HashSet<int>();

            // Query IPv4 TCP Table
            int size4 = 0;
            GetExtendedTcpTable(IntPtr.Zero, ref size4, true, AF_INET, TCP_TABLE_OWNER_PID_ALL, 0);
            if (size4 > 0)
            {
                IntPtr buffer = Marshal.AllocHGlobal(size4);
                try
                {
                    if (GetExtendedTcpTable(buffer, ref size4, true, AF_INET, TCP_TABLE_OWNER_PID_ALL, 0) == 0)
                    {
                        int count = Marshal.ReadInt32(buffer);
                        IntPtr rowPtr = IntPtr.Add(buffer, 4);
                        for (int i = 0; i < count; i++)
                        {
                            int state = Marshal.ReadInt32(rowPtr, 0);
                            int owningPid = Marshal.ReadInt32(rowPtr, 20);
                            // Include Established (5), CloseWait (8), SynSent (2), etc.
                            if (owningPid > 4)
                            {
                                pids.Add(owningPid);
                            }
                            rowPtr = IntPtr.Add(rowPtr, 24);
                        }
                    }
                }
                catch { }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }

            // Query IPv6 TCP Table
            int size6 = 0;
            GetExtendedTcpTable(IntPtr.Zero, ref size6, true, AF_INET6, TCP_TABLE_OWNER_PID_ALL, 0);
            if (size6 > 0)
            {
                IntPtr buffer6 = Marshal.AllocHGlobal(size6);
                try
                {
                    if (GetExtendedTcpTable(buffer6, ref size6, true, AF_INET6, TCP_TABLE_OWNER_PID_ALL, 0) == 0)
                    {
                        int count = Marshal.ReadInt32(buffer6);
                        IntPtr rowPtr = IntPtr.Add(buffer6, 4);
                        for (int i = 0; i < count; i++)
                        {
                            int owningPid = Marshal.ReadInt32(rowPtr, 52); // offset 52 in MIB_TCP6ROW_OWNER_PID
                            if (owningPid > 4)
                            {
                                pids.Add(owningPid);
                            }
                            rowPtr = IntPtr.Add(rowPtr, 56);
                        }
                    }
                }
                catch { }
                finally
                {
                    Marshal.FreeHGlobal(buffer6);
                }
            }

            return pids;
        }

        private CachedProcessDetails? GetProcessDetails(int pid)
        {
            if (_processCache.TryGetValue(pid, out var cached))
                return cached;

            try
            {
                using var proc = Process.GetProcessById(pid);
                string name = proc.ProcessName;
                string exePath = string.Empty;
                BitmapSource? icon = null;

                try
                {
                    exePath = proc.MainModule?.FileName ?? string.Empty;
                }
                catch { }

                if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
                {
                    try
                    {
                        using var sysIcon = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
                        if (sysIcon != null)
                        {
                            icon = Imaging.CreateBitmapSourceFromHIcon(
                                sysIcon.Handle,
                                Int32Rect.Empty,
                                BitmapSizeOptions.FromEmptyOptions());
                            icon.Freeze();
                        }
                    }
                    catch { }
                }

                var details = new CachedProcessDetails
                {
                    Name = name,
                    ExePath = exePath,
                    Icon = icon
                };

                _processCache[pid] = details;
                return details;
            }
            catch
            {
                return null;
            }
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            Stop();
        }
    }
}
