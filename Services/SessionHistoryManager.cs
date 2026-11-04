using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using NetSentry.Models;

namespace NetSentry.Services
{
    public class SessionHistoryManager
    {
        private static readonly string HistoryFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NetSentry");
        private static readonly string HistoryFile = Path.Combine(HistoryFolder, "session_history.json");

        private readonly object _lock = new();
        private List<NetworkSessionRecord> _sessions = new();
        private NetworkSessionRecord? _currentSession;
        private DateTime _lastSavedTime = DateTime.MinValue;

        public NetworkSessionRecord? CurrentSession => _currentSession;

        public SessionHistoryManager()
        {
            LoadHistory();
            StartNewSession();
        }

        private void LoadHistory()
        {
            lock (_lock)
            {
                try
                {
                    if (File.Exists(HistoryFile))
                    {
                        string json = File.ReadAllText(HistoryFile);
                        var loaded = JsonSerializer.Deserialize<List<NetworkSessionRecord>>(json);
                        if (loaded != null)
                        {
                            _sessions = loaded;
                            // Mark any old unclosed session as completed
                            foreach (var s in _sessions)
                            {
                                s.IsCurrentSession = false;
                            }
                        }
                    }
                }
                catch { }
            }
        }

        public void StartNewSession()
        {
            lock (_lock)
            {
                if (_currentSession != null)
                {
                    _currentSession.IsCurrentSession = false;
                    _currentSession.EndTime = DateTime.UtcNow;
                }

                _currentSession = new NetworkSessionRecord
                {
                    StartTime = DateTime.UtcNow,
                    EndTime = DateTime.UtcNow,
                    DownloadBytes = 0,
                    UploadBytes = 0,
                    IsCurrentSession = true
                };

                _sessions.Insert(0, _currentSession);
                SaveToDisk();
            }
        }

        public void UpdateCurrentSession(long rxBytes, long txBytes)
        {
            lock (_lock)
            {
                if (_currentSession == null) return;

                _currentSession.DownloadBytes = rxBytes;
                _currentSession.UploadBytes = txBytes;
                _currentSession.EndTime = DateTime.UtcNow;

                // Save to disk every 10 seconds so data is never lost if PC or app terminates
                if ((DateTime.UtcNow - _lastSavedTime).TotalSeconds >= 10)
                {
                    SaveToDisk();
                }
            }
        }

        public void EndCurrentSession()
        {
            lock (_lock)
            {
                if (_currentSession != null)
                {
                    _currentSession.IsCurrentSession = false;
                    _currentSession.EndTime = DateTime.UtcNow;
                }
                SaveToDisk();
            }
        }

        public List<NetworkSessionRecord> GetAllSessions()
        {
            lock (_lock)
            {
                return _sessions.OrderByDescending(s => s.StartTime).ToList();
            }
        }

        public (long totalRx, long totalTx, long totalCombined, int sessionCount) GetLifetimeStats()
        {
            lock (_lock)
            {
                long rx = _sessions.Sum(s => s.DownloadBytes);
                long tx = _sessions.Sum(s => s.UploadBytes);
                return (rx, tx, rx + tx, _sessions.Count);
            }
        }

        public void ClearOldSessions()
        {
            lock (_lock)
            {
                // Keep only the active session if one exists
                if (_currentSession != null)
                {
                    _sessions = new List<NetworkSessionRecord> { _currentSession };
                }
                else
                {
                    _sessions.Clear();
                }
                SaveToDisk();
            }
        }

        private void SaveToDisk()
        {
            try
            {
                Directory.CreateDirectory(HistoryFolder);
                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(_sessions, options);
                File.WriteAllText(HistoryFile, json);
                _lastSavedTime = DateTime.UtcNow;
            }
            catch { }
        }
    }
}
