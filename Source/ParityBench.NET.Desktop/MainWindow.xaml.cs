using System.Windows;

namespace ParityBench.NET.Desktop;

public partial class MainWindow : Window
{
    public MainWindow(IServiceProvider services)
    {
        Resources.Add("services", services);
        InitializeComponent();
    }
}