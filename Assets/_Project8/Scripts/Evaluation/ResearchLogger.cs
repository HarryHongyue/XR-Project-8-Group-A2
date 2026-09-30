using System;
using System.IO;
using UnityEngine;

namespace Project8.Evaluation
{
    /// <summary>
    /// CSV event logger for the research/evaluation component. Records simulation events,
    /// parameter changes, perspective switches and task outcomes with both simulation and
    /// real timestamps, so observed behaviour can be analysed against model state.
    /// Output: Application.persistentDataPath/Project8Logs/session_*.csv
    /// </summary>
    public class ResearchLogger : MonoBehaviour
    {
        public static ResearchLogger Instance { get; private set; }

        [Tooltip("Simulation clock used for the simulation-time column (optional).")]
        public Core.SimulationController simulation;

        string _path;
        StreamWriter _writer;
        readonly System.Text.StringBuilder _line = new System.Text.StringBuilder();

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            string dir = Path.Combine(Application.persistentDataPath, "Project8Logs");
            Directory.CreateDirectory(dir);
            _path = Path.Combine(dir, $"session_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
            _writer = new StreamWriter(_path);
            _writer.WriteLine("sim_time,real_time,category,detail");
            _writer.Flush();
            Debug.Log($"[Project8] Research log: {_path}");
            LogEvent("Session", "start");
        }

        public void LogEvent(string category, string detail)
        {
            if (_writer == null) return;
            float simT = simulation != null ? simulation.SimulationTime : -1f;
            _line.Clear();
            _line.Append(simT.ToString("F3")).Append(',')
                 .Append(Time.realtimeSinceStartup.ToString("F3")).Append(',')
                 .Append(Escape(category)).Append(',')
                 .Append(Escape(detail));
            _writer.WriteLine(_line.ToString());
            _writer.Flush();
        }

        static string Escape(string s)
            => s != null && (s.Contains(",") || s.Contains("\"") || s.Contains("\n"))
                ? "\"" + s.Replace("\"", "\"\"") + "\""
                : s;

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            _writer?.Dispose();
        }
    }
}
