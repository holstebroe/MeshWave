// Global aliases that resolve WPF vs WinForms ambiguities introduced by UseWindowsForms.
// All existing code continues to use the WPF types by default.
global using Application      = System.Windows.Application;
global using UserControl      = System.Windows.Controls.UserControl;
global using Color            = System.Windows.Media.Color;
global using Brush            = System.Windows.Media.Brush;
global using Brushes          = System.Windows.Media.Brushes;
global using Rectangle        = System.Windows.Shapes.Rectangle;
global using Point            = System.Windows.Point;
global using MouseEventArgs   = System.Windows.Input.MouseEventArgs;
global using KeyEventArgs     = System.Windows.Input.KeyEventArgs;
global using Cursors          = System.Windows.Input.Cursors;
global using ListView         = System.Windows.Controls.ListView;
global using ListBox          = System.Windows.Controls.ListBox;
global using Button           = System.Windows.Controls.Button;
global using ProgressBar      = System.Windows.Controls.ProgressBar;
global using MenuItem         = System.Windows.Controls.MenuItem;
global using ContextMenu      = System.Windows.Controls.ContextMenu;
global using HorizontalAlignment = System.Windows.HorizontalAlignment;
