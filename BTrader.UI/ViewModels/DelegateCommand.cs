using System;
using System.Windows.Input;

namespace BTrader.UI.ViewModels
{
    public class DelegateCommand : ICommand
    {
        private readonly Func<object, bool> canExecute;
        private readonly Action<object> action;

        public event EventHandler CanExecuteChanged;

        public DelegateCommand(Func<object, bool> canExecute, Action<object> action)
        {
            this.canExecute = canExecute;
            this.action = action;
        }

        public bool CanExecute(object parameter)
        {
            return this.canExecute(parameter);
        }

        public void Execute(object parameter)
        {
            this.action(parameter);
        }
    }
}