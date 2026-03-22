using System.Text.Json.Serialization;
using System.Windows.Input;

namespace AIDictationApp.Models
{
    public class RewordingPrompt : AIDictationApp.ViewModels.BaseViewModel
    {
        private string _name = string.Empty;
        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        private string _instructions = string.Empty;
        public string Instructions
        {
            get => _instructions;
            set => SetProperty(ref _instructions, value);
        }

        private bool _applyAutomatically = false;
        public bool ApplyAutomatically
        {
            get => _applyAutomatically;
            set
            {
                if (value && !_applyAutomatically)
                {
                    if (ValidateApplyAutomatically != null && !ValidateApplyAutomatically(this))
                    {
                        // Fire the property changed event asynchronously to force the UI to revert the toggle
                        var dispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
                        if (dispatcher != null)
                        {
                            dispatcher.TryEnqueue(() => OnPropertyChanged(nameof(ApplyAutomatically)));
                        }
                        else
                        {
                            _ = System.Threading.Tasks.Task.Run(async () =>
                            {
                                await System.Threading.Tasks.Task.Delay(50);
                                OnPropertyChanged(nameof(ApplyAutomatically));
                            });
                        }
                        return;
                    }
                }
                SetProperty(ref _applyAutomatically, value);
            }
        }

        private int _order;
        public int Order
        {
            get => _order;
            set => SetProperty(ref _order, value);
        }

        [JsonIgnore]
        public System.Func<RewordingPrompt, bool>? ValidateApplyAutomatically { get; set; }

        [JsonIgnore]
        public ICommand? ApplyCommand { get; set; }

        [JsonIgnore]
        public ICommand? RemoveCommand { get; set; }

        [JsonIgnore]
        public ICommand? MoveUpCommand { get; set; }

        [JsonIgnore]
        public ICommand? MoveDownCommand { get; set; }
    }
}