using Microsoft.VisualBasic.FileIO;
using OpenCvSharp;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Forms;
using Xabe.FFmpeg;
using Xabe.FFmpeg.Downloader;
using MessageBox = System.Windows.MessageBox;
using Point = OpenCvSharp.Point;
using Size = OpenCvSharp.Size;
using Window = System.Windows.Window;

namespace BmwDriveRecorderOverlay
{
    public partial class MainWindow : Window
    {
        private string selectedFolder = "";

        public MainWindow()
        {
            InitializeComponent();
        }

        private void SelectFolder_Click(object sender, RoutedEventArgs e)
        {
            using var dialog = new FolderBrowserDialog();
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                selectedFolder = dialog.SelectedPath;
                FolderPathText.Text = selectedFolder;
            }
        }

        private async void ProcessVideo_Click(object sender, RoutedEventArgs e)
        {
            var files = Directory.EnumerateFiles(selectedFolder);

            string videoFile = "";
            string metadataFile = "";

            foreach (var file in files)
            {
                if (file.EndsWith(".ts", StringComparison.OrdinalIgnoreCase))
                    videoFile = await ConvertTsToMp4(file);
                else if (file.EndsWith("Metadata.json", StringComparison.OrdinalIgnoreCase))
                    metadataFile = file;
            }

            if (string.IsNullOrEmpty(videoFile) || string.IsNullOrEmpty(metadataFile))
            {
                MessageBox.Show("Required files not found in the selected folder.");
                return;
            }

            // Open video
            using var capture = new VideoCapture(videoFile);

            // Load metadata
            string json = File.ReadAllText(metadataFile);
            var vehicles = JsonSerializer.Deserialize<List<VehicleData>>(json);
            if (vehicles == null || vehicles.Count == 0) return;

            var metadataEntries = vehicles[0].Entries;
            if(metadataEntries is null) return;
            int ratio = (int)(metadataEntries.Count / capture.FrameCount);

            // VideoWriter setup
            int width = (int)capture.FrameWidth;
            int height = (int)capture.FrameHeight;
            string outputFile = Path.Combine(selectedFolder, "output.mp4");

            using var writer = new VideoWriter(outputFile, FourCC.MP4V, capture.Fps, new Size(width, height), true);

            // Text overlay setup
            HersheyFonts font = HersheyFonts.HersheySimplex;
            double fontScale = 1.0;
            int thickness = 2;
            int baseline;
            Size textSize = Cv2.GetTextSize("Speed: 000 km/h", font, fontScale, thickness, out baseline);

            int x = 10;
            int y = textSize.Height + 10;

            // Process frames and overlay text
            var frame = new Mat();
            int frameCount = 0;

            while (capture.Read(frame))
            {
                int index = Math.Min(frameCount * ratio, metadataEntries.Count - 1);
                var entry = metadataEntries[index];
                string velocityKph = PadWithZeros(entry.VelocityKmH);

                string text1 = $"Speed: {velocityKph} km/h";
                string text2 = $"{entry.VelocityMpH} mph";

                Cv2.PutText(frame, text1, new Point(x, y), font, fontScale, new Scalar(0, 0, 255), thickness);
                Cv2.PutText(frame, text2, new Point(x, y*2), font, fontScale, new Scalar(0, 0, 255), thickness);
                writer.Write(frame);

                frameCount++;
            }

            MessageBox.Show("Processing complete. Output saved to " + outputFile);
        }



        private async Task<string> ConvertTsToMp4(string tsFilePath)
        {
            if (string.IsNullOrEmpty(tsFilePath) || !File.Exists(tsFilePath))
                throw new FileNotFoundException("Input .ts file not found", tsFilePath);

            // Ensure FFmpeg executables are downloaded
            await FFmpegDownloader.GetLatestVersion(FFmpegVersion.Official);

            string? directory = Path.GetDirectoryName(tsFilePath);
            if (string.IsNullOrEmpty(directory))
                throw new ArgumentException("Could not determine directory for the input file.", nameof(tsFilePath));

            string outputFile = Path.Combine(
                directory,
                Path.GetFileNameWithoutExtension(tsFilePath) + ".mp4"
            );

            var conversion = await FFmpeg.Conversions.New()
                .AddParameter($"-i \"{tsFilePath}\" -c copy \"{outputFile}\"")
                .Start();

            return outputFile;
        }

        private string PadWithZeros(double speed) 
        {
            string speedString = "";
            if (speed < 1)
            {
                speedString = "--0.00";
            }
            else if (speed < 10)
            {
                speedString = "--" + speed.ToString();
            }
            else if (speed < 100)
            {
                speedString = "-" + speed.ToString();
            }
            else 
            {
                speedString = speed.ToString();
            }
                while (speedString.Length < 6)
                {
                    if (speedString.Length == 3)
                    {
                        speedString += ".";
                    }
                    else
                    {
                        speedString += "0";
                    }
                }
            return speedString;
        }
    }

    public class Entry
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("date")]
        public string? Date { get; set; }

        [JsonPropertyName("time")]
        public string? Time { get; set; }

        [JsonPropertyName("latitude")]
        public double Latitude { get; set; }

        [JsonPropertyName("longitude")]
        public double Longitude { get; set; }

        [JsonPropertyName("velocity_KM/H")]
        public double VelocityKmH { get; set; }

        [JsonPropertyName("velocity_MP/H")]
        public string? VelocityMpH { get; set; }
    }

    public class VehicleData
    {
        [JsonPropertyName("VIN")]
        public string? VIN { get; set; }

        [JsonPropertyName("entries")]
        public List<Entry>? Entries { get; set; }
    }
}

