using System;
using System.Windows;

namespace QrLedgerReconciler.ViewModels;

public interface IDialogService
{
    DateTime? ShowDatePicker(DateTime initialDate);
}

public class DialogService : IDialogService
{
    public DateTime? ShowDatePicker(DateTime initialDate)
    {
        var picker = new DateTimePickerWindow(initialDate);
        return picker.ShowDialog() == true ? picker.SelectedDateTime : null;
    }
}