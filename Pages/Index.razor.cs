using KristofferStrube.Blazor.FileAPI;
using KristofferStrube.Blazor.FileSystem;
using KristofferStrube.Blazor.FileSystemAccess;
using KristofferStrube.Blazor.Streams;
using KristofferStrube.Blazor.WebIDL;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Blazor1brc.Pages;

public partial class Index
{
    private bool supported;
    private bool reading;
    private string output = "";
    private long? time;

    [Inject]
    public required IFileSystemAccessServiceInProcess FileSystemAccessService { get; set; }

    [Inject]
    public required IJSRuntime JSRuntime { get; set; }

    protected override async Task OnInitializedAsync()
    {
        supported = await FileSystemAccessService.IsSupportedAsync();
    }

    private StringBuilder workingName = default!;
    private int workingNameHash = 0!;
    private int workingTemperature;
    private bool workingTemperatureNegative;
    private LinePart workingPart;
    private Dictionary<int, CitySummary> cities;
    private Dictionary<int, string> cityNames;

    private async Task ReadFile()
    {
        OpenFilePickerOptionsStartInWellKnownDirectory options = new()
        {
            Multiple = false,
            StartIn = WellKnownDirectory.Downloads
        };

        FileSystemFileHandleInProcess[] fileHandles = await FileSystemAccessService.ShowOpenFilePickerAsync(options);

        time = null;
        reading = true;
        StateHasChanged();
        Stopwatch sw = Stopwatch.StartNew();
        long bytesRead = 0;

        FileSystemFileHandleInProcess fileHandle = fileHandles.Single();
        FileInProcess file = await fileHandle.GetFileAsync();
        ReadableStreamInProcess stream = await file.StreamAsync();
        ReadableStreamDefaultReaderInProcess reader = stream.GetDefaultReader();

        workingName = new(8 * 8);
        workingNameHash = 0;
        workingTemperature = 0;
        workingTemperatureNegative = false; // positive by default
        workingPart = LinePart.Name;
        cities = new();
        cityNames = new();

        while (true)
        {
            ReadableStreamReadResultInProcess read = await reader.ReadAsync();
            if (read.Done)
            {
                break;
            }
            Uint8Array value = await Uint8Array.CreateAsync(JSRuntime, read.Value);
            byte[] bytes = await value.GetAsArrayAsync();
            bytesRead += bytes.LongLength;
            ProcessCharacters(Encoding.UTF8.GetChars(bytes));
            await value.JSReference.DisposeAsync();
            await read.JSReference.DisposeAsync();
        }

        StringBuilder resultBuilder = new(10000);
        resultBuilder.Append("{");
        resultBuilder.AppendJoin(", ", cities.Select(kvp =>
        $"{cityNames[kvp.Key]}={kvp.Value.Min / (float)10:0.##}/{kvp.Value.Sum / (float)10 / kvp.Value.Count:0.##}/{kvp.Value.Max / (float)10:0.##}"
        ));
        resultBuilder.Append("}");

        output = resultBuilder.ToString();
        time = sw.ElapsedMilliseconds;
        reading = false;
    }

    private void ProcessCharacters(char[] characters)
    {
        for (int i = 0; i < characters.Length; i++)
        {
            char character = characters[i];
            if (character is ';')
            {
                workingPart = LinePart.Number;
                workingTemperature = 0;
                workingTemperatureNegative = false;
            }
            else if (character is '\n')
            {
                int temperature = workingTemperature * (workingTemperatureNegative ? -1 : 1);

                ref CitySummary citySummary = ref CollectionsMarshal.GetValueRefOrAddDefault(cities, workingNameHash, out bool exists);

                if (exists)
                {
                    citySummary.Sum += temperature;
                    citySummary.Count++;
                    citySummary.Min = temperature < citySummary.Min ? temperature : citySummary.Min;
                    citySummary.Max = temperature > citySummary.Max ? temperature : citySummary.Max;
                }
                else
                {
                    cityNames.Add(workingNameHash, workingName.ToString());
                    citySummary.Sum = temperature;
                    citySummary.Count = 1;
                    citySummary.Min = temperature;
                    citySummary.Max = temperature;
                }

                workingPart = LinePart.Name;
                workingName.Clear();
                workingNameHash = 0;
            }
            else if (workingPart is LinePart.Name)
            {
                workingNameHash = workingNameHash * 33 + character;
                workingName.Append(character);
            }
            else if (character is '-')
            {
                workingTemperatureNegative = true;
            }
            else if (character is not '.') // Temperature 
            {
                workingTemperature = workingTemperature * 10 + character - '0';
            }
        }
    }

    public enum LinePart
    {
        Name,
        Number
    }

    public struct CitySummary
    {
        public long Sum { get; set; } // 10 multipla of the real sum.
        public int Count { get; set; }
        public long Min { get; set; } // 10 multipla of the real min.
        public long Max { get; set; } // 10 multipla of the real max.
    }
}