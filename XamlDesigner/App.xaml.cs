using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
#if !NETFRAMEWORK
using System.Text.Json;
#endif

using ICSharpCode.WpfDesign.Designer;
using ICSharpCode.XamlDesigner.Configuration;

namespace ICSharpCode.XamlDesigner
{
	public partial class App
	{
		public static string[] Args;

		sealed class DesignerPipeMessage
		{
			public string command { get; set; }
			public string path { get; set; }
			public string xamlText { get; set; }
		}

		/// <summary>The callback pipe name passed via --callback, used to notify VS Code.</summary>
		public static string CallbackPipeName { get; private set; }

		protected override void OnStartup(StartupEventArgs e)
		{
			AppDomain.CurrentDomain.AssemblyResolve += new ResolveEventHandler(AppDomain_CurrentDomain_AssemblyResolve);
			AppDomain.CurrentDomain.UnhandledException += new UnhandledExceptionEventHandler(AppDomain_CurrentDomain_UnhandledException);
			DragDropExceptionHandler.UnhandledException += new ThreadExceptionEventHandler(DragDropExceptionHandler_UnhandledException);
			DispatcherUnhandledException += App_DispatcherUnhandledException;

			// Parse --pipe <name> and --callback <name> before exposing Args to the rest of the app.
			var rawArgs = e.Args.ToList();
			int pipeIdx = rawArgs.IndexOf("--pipe");
			if (pipeIdx >= 0 && pipeIdx + 1 < rawArgs.Count)
			{
				string pipeName = rawArgs[pipeIdx + 1];
				rawArgs.RemoveRange(pipeIdx, 2);
				Thread pipeThread = new Thread(() => RunPipeServer(pipeName)) { IsBackground = true, Name = "PipeServer" };
				pipeThread.Start();
			}

			int cbIdx = rawArgs.IndexOf("--callback");
			if (cbIdx >= 0 && cbIdx + 1 < rawArgs.Count)
			{
				CallbackPipeName = rawArgs[cbIdx + 1];
				rawArgs.RemoveRange(cbIdx, 2);
			}

			Args = rawArgs.ToArray();

			base.OnStartup(e);
		}

		static void RunPipeServer(string pipeName)
		{
			while (true)
			{
				try
				{
					using var pipe = new NamedPipeServerStream(
						pipeName,
						PipeDirection.In,
						/*maxNumberOfServerInstances*/ 1,
						PipeTransmissionMode.Byte,
#if NETFRAMEWORK
						PipeOptions.None);
#else
						PipeOptions.CurrentUserOnly);
#endif

					pipe.WaitForConnection();

					using var reader = new StreamReader(pipe);
					string payload = reader.ReadToEnd();

					var message = ParsePipeMessage(payload);
					if (message != null)
					{
						Application.Current.Dispatcher.Invoke(() =>
						{
							HandlePipeMessage(message);
						});
					}
				}
				catch (Exception)
				{
					// Keep the loop alive; individual connection errors are non-fatal.
				}
			}
		}

		static DesignerPipeMessage ParsePipeMessage(string payload)
		{
			if (string.IsNullOrWhiteSpace(payload))
				return null;

			try
			{
#if NETFRAMEWORK
				return ParsePipeMessageManual(payload);
#else
				return JsonSerializer.Deserialize<DesignerPipeMessage>(payload);
#endif
			}
			catch
			{
				var legacyPath = payload.Trim();
				return string.IsNullOrWhiteSpace(legacyPath)
					? null
					: new DesignerPipeMessage { command = "openFile", path = legacyPath };
			}
		}

#if NETFRAMEWORK
		static DesignerPipeMessage ParsePipeMessageManual(string payload)
		{
			static string Extract(string json, string key)
			{
				var m = Regex.Match(json, "\"" + Regex.Escape(key) + "\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
				if (!m.Success) return null;
				return Regex.Unescape(m.Groups[1].Value);
			}
			return new DesignerPipeMessage
			{
				command  = Extract(payload, "command"),
				path     = Extract(payload, "path"),
				xamlText = Extract(payload, "xamlText"),
			};
		}
#endif

		static void HandlePipeMessage(DesignerPipeMessage message)
		{
			if (message == null || string.IsNullOrWhiteSpace(message.path))
				return;

			if (string.Equals(message.command, "applyXamlText", StringComparison.OrdinalIgnoreCase) &&
			    !string.IsNullOrEmpty(message.xamlText))
			{
				Shell.Instance.OpenOrUpdatePreview(message.path, message.xamlText);
			}
			else
			{
				Shell.Instance.Open(message.path);
			}

			Application.Current.MainWindow?.Activate();
		}

		private static bool internalLoad = false;
		private static string lastRequesting = null;

		Assembly AppDomain_CurrentDomain_AssemblyResolve(object sender, ResolveEventArgs args)
		{
			var assList = AppDomain.CurrentDomain.GetAssemblies();
			var loaded = assList.FirstOrDefault(x => x.FullName == args.Name);
			if (loaded != null)
			{
				return loaded;
			}

			if (internalLoad)
				return null;
			
			if (args.Name.Split(new [] { ',' })[0].Trim().EndsWith(".resources"))
				return null;
			
			internalLoad = true;
			
			Assembly ass = null;
			try {
				
				ass = Assembly.Load(args.Name);
			}
			catch (Exception) { }

			if (ass == null && args.RequestingAssembly != null) {
				lastRequesting = args.RequestingAssembly.Location;
				var dir = Path.GetDirectoryName(args.RequestingAssembly.Location);
				var file = args.Name.Split(new [] { ',' })[0].Trim() + ".dll";
				try {
					ass = Assembly.LoadFrom(Path.Combine(dir, file));
				}
				catch (Exception) { }
			}
			else if (lastRequesting != null) {
				var dir = Path.GetDirectoryName(lastRequesting);
				var file = args.Name.Split(new [] { ',' })[0].Trim() + ".dll";
				try {
					ass = Assembly.LoadFrom(Path.Combine(dir, file));
				}
				catch (Exception) { }
			}
			
			internalLoad = false;

			return ass;
		}
		
		void DragDropExceptionHandler_UnhandledException(object sender, ThreadExceptionEventArgs e)
		{
			Shell.ReportException(e.Exception);
			
		}

		void AppDomain_CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
		{
			Shell.ReportException(e.ExceptionObject as Exception);
		}


		void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
		{
			Shell.ReportException(e.Exception);
			e.Handled = true;
		}

		/// <summary>
		/// Sends a JSON message to VS Code via the callback pipe (fire-and-forget).
		/// Does nothing if no callback pipe was specified.
		/// </summary>
		public static void SendCallbackMessage(string json)
		{
			if (string.IsNullOrEmpty(CallbackPipeName))
				return;

			try
			{
				using var client = new System.IO.Pipes.NamedPipeClientStream(
					".", CallbackPipeName,
					System.IO.Pipes.PipeDirection.Out,
#if NETFRAMEWORK
					System.IO.Pipes.PipeOptions.None);
#else
					System.IO.Pipes.PipeOptions.CurrentUserOnly);
#endif
				client.Connect(2000);
				using var writer = new System.IO.StreamWriter(client);
				writer.Write(json);
			}
			catch
			{
				// Best effort — VS Code may have closed the pipe server.
			}
		}

		protected override void OnExit(ExitEventArgs e)
		{
			Settings.Default.Save();
			base.OnExit(e);
		}
	}
}
