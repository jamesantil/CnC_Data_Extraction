using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

using Sinumerik.Advanced;

namespace CnC_Data;

public partial class MainWindow : Window
{
    bool working = false;

    public MainWindow()
    {
        InitializeComponent();
        LoadParameters();
    }

    // Load parameter names from JSON into ComboBox
    private void LoadParameters()
    {
        var combo = this.FindControl<ComboBox>("ParameterCombo");
        if (combo == null) return;

        string json = File.ReadAllText("config.json");
        using var doc = JsonDocument.Parse(json);
        var parameters = doc.RootElement.GetProperty("parameters");

        var items = new List<ComboBoxItem>();

        foreach (var param in parameters.EnumerateObject())
        {
            var checkbox = new CheckBox
            {
                Content = param.Name
            };

            items.Add(new ComboBoxItem
            {
                Content = checkbox
            });
        }

        combo.ItemsSource = items;
    }

    private async void SubmitClicked(object? sender, RoutedEventArgs e)
    {
        var inputIP = this.FindControl<TextBox>("InputIPBox")?.Text ?? "";
        var frequencyText = this.FindControl<TextBox>("FrequencyBox")?.Text ?? "";
        var combo = this.FindControl<ComboBox>("ParameterCombo");
        var output = this.FindControl<TextBlock>("OutputText");
        var outputcsv = this.FindControl<TextBox>("CSVLocation");

        int frequency = 1000;
        int.TryParse(frequencyText, out frequency);

        // Collect selected parameters
        var selectedParams = new List<string>();

        if (combo?.Items != null)
        {
            foreach (var item in combo.Items)
            {
                if (item is ComboBoxItem comboItem &&
                    comboItem.Content is CheckBox checkBox &&
                    checkBox.IsChecked == true)
                {
                    selectedParams.Add(checkBox.Content?.ToString() ?? "");
                }
            }
        }

        if (selectedParams.Count == 0)
        {
            if (output != null)
                output.Text = "No parameters selected.";
            return;
        }

        // CSV file location
        string csvFile = outputcsv?.Text ?? "output.csv";

        // Always overwrite file
        string header = "timestamp";

        foreach (var param in selectedParams)
            header += "," + param;

        File.WriteAllText(csvFile, header + "\n");

        working = true;

        await Task.Run(() =>
        {
            using var doc = JsonDocument.Parse(File.ReadAllText("config.json"));
            var parameters = doc.RootElement.GetProperty("parameters");

            try
            {
                using var client = new SinumerikClient($"s840d.sl://{inputIP}");
                client.Connect();

                while (working)
                {
                    string resultText = "";
                    string row = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

                    foreach (string keyName in selectedParams)
                    {
                        if (parameters.TryGetProperty(keyName, out JsonElement jsonValue))
                        {
                            string address = jsonValue[0].GetString() ?? "";

                            var value = client.ReadValue(address)?.ToString() ?? "";

                            row += "," + value;
                            resultText += $"{keyName}: {value}\n";
                        }
                    }

                    // write to CSV
                    File.AppendAllText(csvFile, row + "\n");

                    // update GUI safely
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (output != null)
                            output.Text = resultText;
                    });

                    Thread.Sleep(frequency);
                }
            }
            catch (Exception ex)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (output != null)
                        output.Text = $"Connection error: {ex.Message}";
                });
            }
        });
    }

    // Optional stop button
    private void StopClicked(object? sender, RoutedEventArgs e)
    {
        working = false;
    }
}

