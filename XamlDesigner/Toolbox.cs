using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using ICSharpCode.XamlDesigner.Configuration;
using ICSharpCode.WpfDesign;

namespace ICSharpCode.XamlDesigner
{
	public class Toolbox
	{
		static readonly string[] CommonControlOrder = new[] {
			"Button", "TextBox", "TextBlock", "Label", "CheckBox", "RadioButton",
			"ComboBox", "ListBox", "ListView", "TreeView", "Image", "Border",
			"Grid", "StackPanel", "WrapPanel", "DockPanel", "Canvas", "TabControl",
			"GroupBox", "ScrollViewer", "Slider", "ProgressBar", "DatePicker",
			"Menu", "ToolBar", "Expander", "DataGrid", "PasswordBox", "RichTextBox"
		};

		public Toolbox()
		{
			AssemblyNodes = new ObservableCollection<AssemblyNode>();
			ToolboxGroups = new ObservableCollection<ToolboxGroup>();
			ToolboxGroups.Add(new ToolboxGroup("Common Controls"));
			ToolboxGroups.Add(new ToolboxGroup("Custom Controls"));
			AddAssembly(typeof(Button).Assembly.Location, ToolboxGroupKind.Common, false);
			//LoadSettings();
		}

		public static Toolbox Instance = new Toolbox();

		public ObservableCollection<AssemblyNode> AssemblyNodes { get; private set; }

		public ObservableCollection<ToolboxGroup> ToolboxGroups { get; private set; }

		public void AddAssembly(string path)
		{
			AddAssembly(path, ToolboxGroupKind.Custom, true);
		}

		void AddAssembly(string path, ToolboxGroupKind groupKind, bool updateSettings)
		{
			path = Environment.ExpandEnvironmentVariables(path);
			if (AssemblyNodes.Any(node => string.Equals(node.Path, path, StringComparison.OrdinalIgnoreCase))) {
				return;
			}

			var assembly = Assembly.LoadFrom(path);
			MyTypeFinder.Instance.RegisterAssembly(assembly);

			var node = new AssemblyNode {
				Assembly = assembly,
				Path = path,
				GroupKind = groupKind
			};

			foreach (var type in assembly.GetExportedTypes()) {
				if (IsControl(type)) {
					node.Controls.Add(new ControlNode { Type = type, GroupKind = groupKind });
				}
			}

			node.Controls.Sort(CompareControls);

			AssemblyNodes.Add(node);
			RebuildGroups();

			if (updateSettings) {
				if (Settings.Default.AssemblyList == null) {
					Settings.Default.AssemblyList = new StringCollection();
				}
				if (!Settings.Default.AssemblyList.Contains(path)) {
					Settings.Default.AssemblyList.Add(path);
				}
			}
		}

		public void Remove(AssemblyNode node)
		{
			AssemblyNodes.Remove(node);
			Settings.Default.AssemblyList.Remove(node.Path);
			RebuildGroups();
		}

		public void LoadSettings()
		{
			if (Settings.Default.AssemblyList != null) {
				foreach (var path in Settings.Default.AssemblyList) {
					try {
						AddAssembly(path, ToolboxGroupKind.Custom, false);
					}
					catch (Exception) { }
				}
			}
		}

		static bool IsControl(Type type)
		{
			return !type.IsAbstract
				&& !type.IsGenericTypeDefinition
				&& type.IsSubclassOf(typeof(UIElement))
				&& type.GetConstructor(BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null) != null;
		}

		void RebuildGroups()
		{
			foreach (var group in ToolboxGroups) {
				group.Controls.Clear();
			}

			foreach (var control in AssemblyNodes
				.SelectMany(node => node.Controls)
				.OrderBy(control => control, Comparer<ControlNode>.Create(CompareControls))) {
				var group = control.GroupKind == ToolboxGroupKind.Common ? ToolboxGroups[0] : ToolboxGroups[1];
				group.Controls.Add(control);
			}
		}

		static int CompareControls(ControlNode left, ControlNode right)
		{
			if (left.GroupKind == ToolboxGroupKind.Common && right.GroupKind == ToolboxGroupKind.Common) {
				var leftIndex = Array.IndexOf(CommonControlOrder, left.Name);
				var rightIndex = Array.IndexOf(CommonControlOrder, right.Name);
				if (leftIndex >= 0 || rightIndex >= 0) {
					leftIndex = leftIndex >= 0 ? leftIndex : int.MaxValue;
					rightIndex = rightIndex >= 0 ? rightIndex : int.MaxValue;
					if (leftIndex != rightIndex) {
						return leftIndex.CompareTo(rightIndex);
					}
				}
			}

			return string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
		}
	}

	public enum ToolboxGroupKind
	{
		Common,
		Custom
	}

	public class ToolboxGroup
	{
		public ToolboxGroup(string name)
		{
			Name = name;
			Controls = new ObservableCollection<ControlNode>();
		}

		public string Name { get; private set; }

		public ObservableCollection<ControlNode> Controls { get; private set; }
	}

	public class AssemblyNode
	{
		public AssemblyNode()
		{
			Controls = new List<ControlNode>();
		}

		public Assembly Assembly { get; set; }
		public List<ControlNode> Controls { get; private set; }
		public string Path { get; set; }
		public ToolboxGroupKind GroupKind { get; set; }

		public string Name {
			get { return Assembly.GetName().Name; }
		}
	}

	public class ControlNode
	{
		public Type Type { get; set; }
		public ToolboxGroupKind GroupKind { get; set; }

		public string Name {
			get { return Type.Name; }
		}

		public string Glyph {
			get {
				var capitals = new string(Name.Where(char.IsUpper).Take(3).ToArray());
				if (!string.IsNullOrEmpty(capitals)) {
					return capitals;
				}

				return Name.Length <= 3
					? Name.ToUpperInvariant()
					: Name.Substring(0, 3).ToUpperInvariant();
			}
		}

		public string AccentColor {
			get {
				switch (Name) {
					case "Button":
					case "CheckBox":
					case "RadioButton":
						return "#2F6FED";
					case "TextBox":
					case "RichTextBox":
					case "PasswordBox":
						return "#11875D";
					case "Grid":
					case "StackPanel":
					case "WrapPanel":
					case "DockPanel":
					case "Canvas":
					case "Border":
						return "#C66A10";
					case "ListBox":
					case "ListView":
					case "TreeView":
					case "DataGrid":
						return "#8B3FB3";
					default:
						return GroupKind == ToolboxGroupKind.Custom ? "#A34A2C" : "#5C6B73";
				}
			}
		}
	}
}
