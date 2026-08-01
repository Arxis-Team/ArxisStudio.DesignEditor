namespace DesignEditor.Demo.ViewModels;

public class FormElementViewModel : DesignItemViewModel
{
    public FormElementViewModel(double x, double y) : base(x, y)
    {
        Width = 320;
        Height = 260;
    }
}
