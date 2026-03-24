using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using Sinumerik.Advanced;
using Avalonia;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Net.Http;
using System.Text;
using System.Linq;
using System.Net.NetworkInformation;

namespace CnC_Data;

class Program
{
    static HttpClient client = new HttpClient();

    public static void Main(string[] args)
    {
        string? ip = null;
        string? mac= null;
        string? f = null;
        int freq = 0;

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--ip" && i + 1 < args.Length)
                ip = args[i + 1];
            if (args[i] == "--f" && i + 1 < args.Length)
                f = args[i + 1];
            if (args[i] == "--freq" && i + 1 < args.Length)
                int.TryParse(args[i + 1], out freq);
            if (args[i] == "--mac" && i + 1 < args.Length)
                mac=args[i + 1];
        }

        if (!string.IsNullOrEmpty(ip))
        {
            RunHeadless(ip,mac, f ?? "", freq);
            return;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /* ---------------- HTTP POST ---------------- */

    static async Task SendJSON(string url, string json, string ip)
    {
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        try
        {
            var response = await client.PostAsync(url, content);
            var responseText = await response.Content.ReadAsStringAsync();

            Console.WriteLine($"[{ip}] HTTP Status: {response.StatusCode}");
            Console.WriteLine($"Response: {responseText}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"POST request failed: {ex.Message}");
        }
    }

    /* ---------------- PING CHECK ---------------- */

    static bool CheckPing(string ip)
    {
        using var ping = new Ping();

        try
        {
            PingReply reply = ping.Send(ip, 2000);

            if (reply.Status == IPStatus.Success)
            {
                Console.WriteLine($"Ping OK ({reply.RoundtripTime} ms)");
                return true;
            }
            else
            {
                Console.WriteLine($"Ping failed: {reply.Status}");
                return false;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ping error: {ex.Message}");
            return false;
        }
    }

    /* ---------------- HEADLESS MODE ---------------- */

    static void RunHeadless(string ip,string mac, string f, int freq)
    {
        Console.WriteLine($"Starting monitoring for {ip}");

        var doc = JsonDocument.Parse(File.ReadAllText("config.json"));
        var parameters = doc.RootElement.GetProperty("parameters");

        string jsonFile;

        if (string.IsNullOrEmpty(f))
        {
            jsonFile = string.Join("_", ip.Split('.')) + "_machine_log.json";
        }
        else
        {
            jsonFile = f;
        }

        /* -------- WAIT UNTIL MACHINE RESPONDS -------- */

        while (!CheckPing(ip))
        {
            Console.WriteLine("Machine not reachable");
                string machineoff = $@"{{
    ""mac_address"": ""{mac}"",
    ""data"": {{
        ""dmg_data"": [
        {{
            ""machine_status"" : ""off"",
            ""start_up"" : null,
            ""total_number"" : null,
            ""current_cycle"" : 0,
            ""current_run_time"" : 0,
            ""total_cutting_time"" : 0,
            ""tool_name"" : null,
            ""tool_radius"" : null,
            ""tool_length"" : null,
            ""current_program_name"" : null,
            ""current_program_content"" : null,
            ""spindle_speed"" : 0,
            ""spindle_feed"" : 0,
            ""spindle_override"" : 0,
            ""feed_override"" : 0,
            ""x_axis_coordinate"" : 0,
            ""y_axis_coordinate"" : 0,
            ""z_axis_coordinate"" : 0,
            ""c_axis_coordinate"" : 0,
            ""a_axis_coordinate"" : 0,
            ""driveload_x"" : 0,
            ""driveload_y"" : 0,
            ""driveload_z"" : 0,
            ""driveload_c"" : 0,
            ""driveload_a"" : 0,
            ""program_status"" : 0,
            ""spindle_load"" : 0
        }}]
    }}
}}";
             

            _ = SendJSON(
                "https://dev.zwillinglabs.com/node/api/iot/data",
                machineoff,
                ip
            );

            Thread.Sleep(5000);
        }

        Console.WriteLine("Machine reachable. Connecting to CNC...");

        using var cnc = new SinumerikClient($"s840d.sl://{ip}");
        cnc.Connect();

        Console.WriteLine("Connected.");

        /* -------- MAIN LOOP -------- */

        while (true)
        {
            var data = new Dictionary<string, object?>();

            foreach (var param in parameters.EnumerateObject())
            {
                string name = param.Name;
                string address = param.Value[0].GetString() ?? "";

                try
                {
                    var value = cnc.ReadValue(address);
                    data[name] = value;
                }
                catch
                {
                    data[name] = null;
                }
            }

            /* -------- JSON FOR API -------- */

           var topost = new
{
    mac_address = mac,
    data = new
    {
        dmg_data = new[]
        {
            new Dictionary<string, object?>(data)
            {
                ["machine_status"] = "on"
            }
        }
    }
};
           
            string postjson = JsonSerializer.Serialize(topost,
                new JsonSerializerOptions { WriteIndented = true });

          

            /* -------- SEND DATA -------- */

            _ = SendJSON(
                "https://dev.zwillinglabs.com/node/api/iot/data",
                postjson,
                ip
            );

            Thread.Sleep(freq == 0 ? 3000 : freq);
        }
    }

    /* ---------------- AVALONIA UI ---------------- */

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
