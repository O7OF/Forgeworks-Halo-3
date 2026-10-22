using Avalonia.Controls;
using ForgeWorks.UI.ViewModels;

namespace ForgeWorks.UI.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.RequestOpenTagEditorWindow += () =>
            {
                var win = new TagEditorWindow { DataContext = vm };
                win.Show(this);
            };
        }
    }
}