using System;
using System.ComponentModel;
using System.Text.Json;
using ICSharpCode.WpfDesign;

namespace ICSharpCode.XamlDesigner
{
	/// <summary>
	/// Implements <see cref="IEventHandlerService"/> for the VS Code integration.
	/// On double-click (or Enter in the event editor), this service:
	///   1. Sets the event attribute value in the XAML (e.g. Click="Button_Click").
	///   2. Sends a callback message to VS Code via the named pipe so VS Code can
	///      insert the method stub in the code-behind file and navigate to it.
	/// </summary>
	internal sealed class VsCodeEventHandlerService : IEventHandlerService
	{
		readonly string _xamlPath;

		public VsCodeEventHandlerService(string xamlPath)
		{
			_xamlPath = xamlPath;
		}

		/// <inheritdoc />
		public DesignItemProperty GetDefaultEvent(DesignItem item)
		{
			EventDescriptor descriptor = TypeDescriptor.GetDefaultEvent(item.ComponentType);
			if (descriptor == null)
				return null;

			return item.Properties.GetProperty(descriptor.Name);
		}

		/// <inheritdoc />
		public void CreateEventHandler(DesignItemProperty eventProperty)
		{
			string handlerName = GenerateHandlerName(eventProperty);

			// Write the handler name into the XAML attribute inside an undo-able change group.
			using var changeGroup = eventProperty.DesignItem.Context.OpenGroup(
				"Create event handler", new[] { eventProperty.DesignItem });
			eventProperty.SetValue(handlerName);
			changeGroup.Commit();

			// Notify VS Code to insert the stub and navigate to it.
			string eventArgType = GetEventArgTypeFullName(eventProperty);
			var payload = JsonSerializer.Serialize(new
			{
				command = "createEventHandler",
				xamlPath = _xamlPath,
				handlerName,
				eventName = eventProperty.Name,
				eventArgType,
			});
			App.SendCallbackMessage(payload);
		}

		// -----------------------------------------------------------------------

		static string GenerateHandlerName(DesignItemProperty eventProperty)
		{
			var item = eventProperty.DesignItem;

			// Prefer x:Name; fall back to the simple type name.
			string elementName = item.Properties.GetProperty("Name")?.ValueOnInstance as string;
			if (string.IsNullOrWhiteSpace(elementName))
				elementName = item.ComponentType.Name;

			return $"{elementName}_{eventProperty.Name}";
		}

		static string GetEventArgTypeFullName(DesignItemProperty eventProperty)
		{
			var eventInfo = eventProperty.DesignItem.ComponentType.GetEvent(eventProperty.Name);
			if (eventInfo != null)
			{
				var invokeMethod = eventInfo.EventHandlerType?.GetMethod("Invoke");
				var parameters = invokeMethod?.GetParameters();
				if (parameters?.Length >= 2)
					return parameters[1].ParameterType.FullName ?? "System.EventArgs";
			}
			return "System.EventArgs";
		}
	}
}
