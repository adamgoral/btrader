using BTrader.UI.ViewModels;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace BTrader.UI.Views
{
    /// <summary>
    /// Interaction logic for FootballMatchNavigatorView.xaml
    /// </summary>
    public partial class FootballMatchNavigatorView : UserControl
    {
        public FootballMatchNavigatorView()
        {
            InitializeComponent();
            if (!DesignerProperties.GetIsInDesignMode(this))
            {
                this.DataContext = App.GetViewModel<FootballMatchNavigatorViewModel>();
            }
        }
    }
}
