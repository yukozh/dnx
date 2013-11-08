﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WatchDog
{
    public class Executable
    {
        public Executable(string path, string workingDirectory, TimeSpan idleTimeout)
        {
            Path = path;
            WorkingDirectory = workingDirectory;
            EnvironmentVariables = new Dictionary<string, string>();
            Encoding = Encoding.UTF8;
            IdleTimeout = idleTimeout;
        }

        public bool IsAvailable
        {
            get
            {
                return File.Exists(Path);
            }
        }

        public string WorkingDirectory { get; private set; }

        public string Path { get; private set; }

        public IDictionary<string, string> EnvironmentVariables { get; set; }

        public Encoding Encoding { get; set; }

        public TimeSpan IdleTimeout { get; private set; }

        public int Execute(Func<string, bool> onWriteOutput, Func<string, bool> onWriteError, Encoding encoding, string arguments, params object[] args)
        {
            Process process = CreateProcess(arguments, args);
            process.EnableRaisingEvents = true;

            var errorBuffer = new StringBuilder();
            var outputBuffer = new StringBuilder();

            process.OutputDataReceived += (sender, e) =>
            {
                if (e.Data != null)
                {
                    if (onWriteOutput(e.Data))
                    {
                        outputBuffer.AppendLine(Encoding.UTF8.GetString(encoding.GetBytes(e.Data)));
                    }
                }
            };

            process.ErrorDataReceived += (sender, e) =>
            {
                if (e.Data != null)
                {
                    if (onWriteError(e.Data))
                    {
                        errorBuffer.AppendLine(Encoding.UTF8.GetString(encoding.GetBytes(e.Data)));
                    }
                }
            };

            process.Start();

            process.BeginErrorReadLine();
            process.BeginOutputReadLine();


            process.WaitForExit();

            return process.ExitCode;
        }

        internal Process CreateProcess(string arguments, object[] args)
        {
            return CreateProcess(String.Format(arguments, args));
        }

        internal Process CreateProcess(string arguments)
        {
            var psi = new ProcessStartInfo
            {
                FileName = Path,
                WorkingDirectory = WorkingDirectory,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                UseShellExecute = false,
                ErrorDialog = false,
                Arguments = arguments
            };

            if (Encoding != null)
            {
                psi.StandardOutputEncoding = Encoding;
                psi.StandardErrorEncoding = Encoding;
            }

            foreach (var pair in EnvironmentVariables)
            {
                psi.EnvironmentVariables[pair.Key] = pair.Value;
            }

            var process = new Process()
            {
                StartInfo = psi
            };

            return process;
        }
    }
}
