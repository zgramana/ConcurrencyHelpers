// ===========================================================
// Copyright (c) 2014-2015, Enrico Da Ros/kendar.org
// All rights reserved.
// 
// Redistribution and use in source and binary forms, with or without
// modification, are permitted provided that the following conditions are met:
// 
// * Redistributions of source code must retain the above copyright notice, this
//   list of conditions and the following disclaimer.
// 
// * Redistributions in binary form must reproduce the above copyright notice,
//   this list of conditions and the following disclaimer in the documentation
//   and/or other materials provided with the distribution.
// 
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
// AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
// IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
// DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE
// FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
// DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
// SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
// CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY,
// OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
// OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
// ===========================================================


using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace CoroutinesLib.Shared.Logging
{
	internal class LogManage
	{
		public LogManage(ILogger logger, bool add)
		{
			Logger = logger;
			Add = add;
		}
		public ILogger Logger { get; private set; }
		public bool Add { get; private set; }
	}
	internal class LogMessage
	{
		public LogMessage(Exception exception, string message, object[] parameters, LoggerLevel level)
		{
			Exception = exception;
			Message = message;
			Parameters = parameters;
			Level = level;
		}
		public Exception Exception { get; private set; }
		public string Message { get; private set; }
		public object[] Parameters { get; private set; }
		public LoggerLevel Level { get; private set; }
	}
	public class LogContainer : ILogger, IDisposable
	{
		private const int LOG_WAIT_MS = 250;
		private readonly ConcurrentQueue<object> _messages;
		private Thread _thread;

		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}
		// NOTE: Leave out the finalizer altogether if this class doesn't 
		// own unmanaged resources itself, but leave the other methods
		// exactly as they are. 
		~LogContainer()
		{
			// Finalizer calls Dispose(false)
			Dispose(false);
		}
		// The bulk of the clean-up code is implemented in Dispose(bool)
		protected virtual void Dispose(bool disposing)
		{
			if (disposing)
			{
				// free managed resources
				if (_thread != null)
				{
					_thread.Abort();
					_thread = null;
				}
			}
		}

		public LogContainer()
		{
			_thread = new Thread(Run);
			_messages = new ConcurrentQueue<object>();
			_thread.Start();
		}

		public LoggerLevel Level { get; private set; }
		public void Fatal(Exception ex, string format = null, params object[] pars)
		{
			_messages.Enqueue(new LogMessage(ex, format, pars, LoggerLevel.Fatal));
		}

		public void Debug(Exception ex, string format = null, params object[] pars)
		{
			_messages.Enqueue(new LogMessage(ex, format, pars, LoggerLevel.Debug));
		}

		public void Error(Exception ex, string format = null, params object[] pars)
		{
			_messages.Enqueue(new LogMessage(ex, format, pars, LoggerLevel.Error));
		}

		public void Fatal(string format, params object[] pars)
		{
			_messages.Enqueue(new LogMessage(null, format, pars, LoggerLevel.Fatal));
		}

		public void Debug(string format, params object[] pars)
		{
			_messages.Enqueue(new LogMessage(null, format, pars, LoggerLevel.Debug));
		}

		public void Error(string format, params object[] pars)
		{
			_messages.Enqueue(new LogMessage(null, format, pars, LoggerLevel.Error));
		}

		public void Warning(string format, params object[] pars)
		{
			_messages.Enqueue(new LogMessage(null, format, pars, LoggerLevel.Warning));
		}

		public void Info(string format, params object[] pars)
		{
			_messages.Enqueue(new LogMessage(null, format, pars, LoggerLevel.Info));
		}

		public void Run()
		{
			var waiter = new ManualResetEvent(false);
			try
			{
				//var sw = new Stopwatch();
				var go = true;
				while (go)
				{
					//sw.Start();
					object result = null;
					int count = 0;

					try
					{
						while (_messages.TryDequeue(out result))
						{
							count++;
							var log = result as LogMessage;
							if (log != null)
							{
								HandleLogMessage(log);
							}
							else
							{
								var logMan = result as LogManage;
								if (logMan != null)
								{
									HandleLogManage(logMan);
								}
							}
						}
					}
					catch (Exception ex)
					{
						Trace.WriteLine(ex);
					}
					/*var elapsed = sw.ElapsedMilliseconds;
					if (elapsed < LOG_WAIT_MS)
					{
						waiter.WaitOne((int) (LOG_WAIT_MS - elapsed));
					}
					else
					{*/
						waiter.WaitOne(LOG_WAIT_MS);
					//}
				}
			}
			catch (ThreadAbortException)
			{
				Warning("LogContainer thread aborted.");
				Thread.ResetAbort();
			}
		}

		private readonly List<ILogger> _loggers = new List<ILogger>();
		private string _logFilePath;

		private void HandleLogMessage(LogMessage log)
		{
			if (_loggers.Count == 0)
			{
				HandleConsoleLogMessage(log);
			}
			foreach (var logger in _loggers)
			{
				HandleLogMessage(log, logger);
			}
		}

		private void HandleLogManage(LogManage logMan)
		{
			var logger = logMan.Logger;
			logger.SetLoggingLevel(Level);
			if (logMan.Add)
			{
				_loggers.Add(logger);
			}
			else
			{
				var idx = _loggers.FindIndex((a) => ReferenceEquals(a, logger));
				if (idx>0)
				{
					_loggers.RemoveAt(idx);
				}
			}
		}

		public void RegisterLogger(ILogger logger)
		{
			_messages.Enqueue(new LogManage(logger, true));
		}

		public void UnregisterLogger(ILogger logger)
		{
			_messages.Enqueue(new LogManage(logger, false));
		}

		private void HandleConsoleLogMessage(LogMessage log)
		{
			if ((int)log.Level > (int)Level) return;
			var color = Console.ForegroundColor;
			if (log.Exception != null)
			{
				Console.ForegroundColor = ConsoleColor.Red;
				Console.WriteLine();
			}
			var now = DateTime.UtcNow.ToString("yyyy-MM-dd hh:mm:ss");
			switch (log.Level)
			{
				case (LoggerLevel.Debug):
					
					WriteLine("D "+now+": "+log.Message, log.Parameters);
					if(log.Exception!=null)WriteLine(log.Exception);
					break;
				case (LoggerLevel.Warning):
					Console.ForegroundColor = ConsoleColor.Yellow;
					WriteLine("W " + now + ": " + log.Message, log.Parameters);
					Console.ForegroundColor = color;
					break;
				case (LoggerLevel.Error):
					Console.ForegroundColor = ConsoleColor.Red;
					WriteLine("E " + now + ": " + log.Message, log.Parameters);
					if (log.Exception != null) WriteLine(log.Exception);
					Console.ForegroundColor = color;
					break;
				case (LoggerLevel.Fatal):
					Console.ForegroundColor = ConsoleColor.DarkRed;
					WriteLine("F " + now + ": " + log.Message, log.Parameters);
					if (log.Exception != null) WriteLine(log.Exception);
					Console.ForegroundColor = color;
					break;
				case (LoggerLevel.Info):
					WriteLine("I " + now + ": " + log.Message, log.Parameters);
					Console.ForegroundColor = color;
					break;
			}
		}

		private void WriteLine(object format,params object[] parameters)
		{
			Console.WriteLine(format.ToString(),parameters);
			if (_logFilePath == null) return;
			File.AppendAllText(_logFilePath, string.Format(format.ToString(), parameters) + "\r\n");
		}

		private void HandleLogMessage(LogMessage log, ILogger logger)
		{
			if ((int)log.Level > (int)Level) return;
			switch (log.Level)
			{
				case (LoggerLevel.Debug):
					logger.Debug(log.Exception, log.Message, log.Parameters);
					break;
				case (LoggerLevel.Warning):
					logger.Warning(log.Message, log.Parameters);
					break;
				case (LoggerLevel.Error):
					logger.Error(log.Exception, log.Message, log.Parameters);
					break;
				case (LoggerLevel.Fatal):
					logger.Fatal(log.Exception, log.Message, log.Parameters);
					break;
				case (LoggerLevel.Info):
					logger.Info(log.Message, log.Parameters);
					break;
			}
		}

		public void SetLoggingLevel(LoggerLevel loggingLevel)
		{
			Level = loggingLevel;
		}

		public void SetLogFile(string logFilePath)
		{
			_logFilePath = logFilePath;
		}
	}
}
