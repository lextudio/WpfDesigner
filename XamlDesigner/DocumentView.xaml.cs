using ICSharpCode.WpfDesign.Designer.Services;

namespace ICSharpCode.XamlDesigner
{
	public partial class DocumentView
	{
		public DocumentView()
		{
			InitializeComponent();
			this.Loaded += DocumentView_Loaded;
		}

		private void DocumentView_Loaded(object sender, System.Windows.RoutedEventArgs e)
		{
			this.Loaded -= DocumentView_Loaded;

			Document = (Document) this.DataContext;
			Shell.Instance.Views[Document] = this;

			Document.Mode = DocumentMode.Design;
		}

		public Document Document { get; private set; }

		public void JumpToError(XamlError error)
		{
			// No XAML source view is hosted here (AvalonEdit has been removed — see
			// DocumentView.xaml), so there is no text position to scroll/select.
			// Document.Mode stays Design; the corresponding design-surface element,
			// if any, is selected independently via XamlErrorService.
		}
	}
}
