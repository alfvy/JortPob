﻿using HKX2;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading.Tasks;

namespace JortPob.Common
{
    public class Lort
    {
        public static ConcurrentBag<string> mainOutput;
        public static ConcurrentBag<string> debugOutput;
        public static string progressOutput;
        public static int total, current;
        public static bool update;
        public static string logFilePath;

        public static void Initialize()
        {
            mainOutput = new();
            debugOutput = new();
            progressOutput = string.Empty;
            total = 0;
            current = 0;
            update = false;
            logFilePath = Const.OUTPUT_PATH + $"jortpob-log-{DateTime.UtcNow.ToLongTimeString().Replace(":", "").Replace(" PM", "")}.txt";
            File.WriteAllText(logFilePath, "");
        }

        public enum Type
        {
            Main,
            Debug
        }

        public static void Log(string message, Lort.Type type)
        {
            switch(type)
            {
                case Type.Main:
                    mainOutput.Add(message); break;
                case Type.Debug:
                    debugOutput.Add(message); break;
            }
            update = true;
            AppendTextToLog(message, type);
        }

        public static void NewTask(string task, int max)
        {
            progressOutput = $"{task}";
            current = 0;
            total = max;
            update = true;
        }

        public static void TaskIterate()
        {
            current = Math.Min(current+1, total);
            update = true;
        }

        private static void AppendTextToLog(string message, Type type)
        {
            switch (type)
            {
                case Type.Main:
                    Task.Run(async () => await File.AppendAllTextAsync(logFilePath, $"[MAIN] {message}\n"));
                    break;
                case Type.Debug:
                    Task.Run(async () => await File.AppendAllTextAsync(logFilePath, $"[DEBUG] {message}\n"));
                    break;
            }
        }
    }
}
