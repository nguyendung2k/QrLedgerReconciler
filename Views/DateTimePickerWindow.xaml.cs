using System;
using System.Windows;

namespace QrLedgerReconciler;

public partial class DateTimePickerWindow : Window
{
    public DateTime SelectedDateTime { get; private set; }

    public DateTimePickerWindow(DateTime currentDate)
    {
        InitializeComponent();

        SelectedDateTime = currentDate;

        // Set ngày hiện tại cho Calendar
        Calendar.SelectedDate = currentDate.Date;
        Calendar.DisplayDate = currentDate.Date;

        // Khởi tạo ComboBox Giờ
        for (int i = 0; i < 24; i++)
        {
            HourCombo.Items.Add(i.ToString("00"));
        }

        // Khởi tạo ComboBox Phút
        for (int i = 0; i < 60; i++)
        {
            MinuteCombo.Items.Add(i.ToString("00"));
        }

        // Set giá trị mặc định
        HourCombo.SelectedItem = currentDate.Hour.ToString("00");
        MinuteCombo.SelectedItem = currentDate.Minute.ToString("00");
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (Calendar.SelectedDate == null)
        {
            MessageBox.Show("Vui lòng chọn ngày.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (HourCombo.SelectedItem == null || MinuteCombo.SelectedItem == null)
        {
            MessageBox.Show("Vui lòng chọn giờ và phút.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        int hour = int.Parse(HourCombo.SelectedItem.ToString()!);
        int minute = int.Parse(MinuteCombo.SelectedItem.ToString()!);

        var selectedDate = Calendar.SelectedDate.Value;

        SelectedDateTime = new DateTime(
            selectedDate.Year,
            selectedDate.Month,
            selectedDate.Day,
            hour,
            minute,
            0);

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}