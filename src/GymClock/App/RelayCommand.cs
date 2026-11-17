using System;
using System.Windows.Input;

namespace GymClock.Wpf
{
    /// <summary>A command that runs a delegate. WPF asks <see cref="CanExecute"/> again whenever input happens.</summary>
    public sealed class RelayCommand : ICommand
    {
        private readonly Action<object> _run;
        private readonly Func<object, bool> _can;

        public RelayCommand(Action<object> run, Func<object, bool> can = null)
        {
            _run = run;
            _can = can;
        }

        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        public bool CanExecute(object parameter)
        {
            return _can == null || _can(parameter);
        }

        public void Execute(object parameter)
        {
            _run(parameter);
        }
    }
}
