using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ICSharpCode.WpfDesign.Designer.OutlineView;
using ICSharpCode.WpfDesign.Designer.Services;
using Microsoft.Win32;

namespace ICSharpCode.XamlDesigner
{
	public partial class ToolboxView
	{
		public ToolboxView()
		{
			DataContext = Toolbox.Instance;
			InitializeComponent();

			new DragListener(this).DragStarted += Toolbox_DragStarted;
		}

		void Toolbox_DragStarted(object sender, MouseButtonEventArgs e)
		{
			PrepareTool(e.GetDataContext() as ControlNode, true);
		}

		void ControlTile_Click(object sender, RoutedEventArgs e)
		{
			PrepareTool((sender as FrameworkElement)?.DataContext as ControlNode, false);
		}

		void PrepareTool(ControlNode node, bool drag)
		{
			if (node != null) {
				var tool = new CreateComponentTool(node.Type);
				if (Shell.Instance.CurrentDocument != null) {
					Shell.Instance.CurrentDocument.DesignContext.Services.Tool.CurrentTool = tool;
					if (drag) {
						DragDrop.DoDragDrop(this, tool, DragDropEffects.Copy);
					}
				}
			}
		}

		private void BrowseForAssemblies_OnClick(object sender, RoutedEventArgs e)
		{
			var dlg = new OpenFileDialog();
			dlg.Filter = "Assemblies (*.dll)|*.dll";
			dlg.Multiselect = true;
			dlg.CheckFileExists = true;
			if (dlg.ShowDialog().Value)
			{
				foreach (var fileName in dlg.FileNames)
				{
					Toolbox.Instance.AddAssembly(fileName);
				}
			}
		}
	}
}
