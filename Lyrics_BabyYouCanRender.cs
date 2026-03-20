// Lyric timing for LyricsBabyYouCanRender
namespace AudioWaveform
{
    public static class LyricsBabyYouCanRender
    {
        public struct LyricLine { public int StartMs; public int EndMs; public string Text; }
        public static readonly LyricLine[] Lines =
        {
            new LyricLine { StartMs =  11100, EndMs =  14150, Text = "Baby I was tired of charts that froze" },
            new LyricLine { StartMs =  14200, EndMs =  17110, Text = "Dropped frames and lag wherever the data goes" },
            new LyricLine { StartMs =  17160, EndMs =  20150, Text = "Tried every library, paid every fee" },
            new LyricLine { StartMs =  20200, EndMs =  22870, Text = "Subscription servers calling back to me" },
            new LyricLine { StartMs =  22920, EndMs =  25890, Text = "Then I found something built since '93" },
            new LyricLine { StartMs =  25940, EndMs =  28530, Text = "Perpetual license, no keys, running free" },
            new LyricLine { StartMs =  28580, EndMs =  31730, Text = "Loaded up a hundred million points one night" },
            new LyricLine { StartMs =  31780, EndMs =  34730, Text = "Fifteen milliseconds and the render looked right" },
            new LyricLine { StartMs =  34780, EndMs =  37450, Text = "GPU shaders burning, Direct3D alive" },
            new LyricLine { StartMs =  37500, EndMs =  40350, Text = "Zero copy pointer, watch that data drive" },
            new LyricLine { StartMs =  40400, EndMs =  43310, Text = "Wellbore flying, radar on the screen" },
            new LyricLine { StartMs =  43360, EndMs =  47950, Text = "Prettiest charting software I've ever seen" },
            new LyricLine { StartMs =  48000, EndMs =  51650, Text = "Baby you can render" },
            new LyricLine { StartMs =  51700, EndMs =  54630, Text = "Baby you can render" },
            new LyricLine { StartMs =  54680, EndMs =  59490, Text = "Baby you can render with me-e-e" },
            new LyricLine { StartMs =  59540, EndMs =  62770, Text = "Hundred million points and the GPU's free" },
            new LyricLine { StartMs =  62820, EndMs =  65430, Text = "GigaSoft, yeah that's where I wanna be" },
            new LyricLine { StartMs =  65480, EndMs =  71500, Text = "Baby you can render with me" },
            new LyricLine { StartMs =  71550, EndMs =  74950, Text = "PsyChart hit me up with a monthly bill" },
            new LyricLine { StartMs =  75000, EndMs =  77750, Text = "LighterChart trial ran out — gave me the chill" },
            new LyricLine { StartMs =  77800, EndMs =  81670, Text = "DexEspress wanted my soul on a plate" },
            new LyricLine { StartMs =  81720, EndMs =  83450, Text = "I called up GigaSoft — engineer picked up straight" },
            new LyricLine { StartMs =  83500, EndMs =  86350, Text = "Dropped perfect code right into my project" },
            new LyricLine { StartMs =  86400, EndMs =  89350, Text = "Oscilloscope zoomed in — man I had to respect" },
            new LyricLine { StartMs =  89400, EndMs =  92450, Text = "Three dimensions, heatmaps, financial too" },
            new LyricLine { StartMs =  92500, EndMs =  97670, Text = "Thirty years of knowing exactly what to do" },
            new LyricLine { StartMs =  97720, EndMs = 100750, Text = "Baby you can render" },
            new LyricLine { StartMs = 100800, EndMs = 103790, Text = "Baby you can render" },
            new LyricLine { StartMs = 103840, EndMs = 108580, Text = "Baby you can render with me-e-e" },
            new LyricLine { StartMs = 108630, EndMs = 111870, Text = "Hundred million points and the GPU's free" },
            new LyricLine { StartMs = 111920, EndMs = 115390, Text = "GigaSoft, yeah that's where I wanna be" },
            new LyricLine { StartMs = 115440, EndMs = 131880, Text = "Baby you can render with me" },
            new LyricLine { StartMs = 131930, EndMs = 138110, Text = "You know they say the best things in life are free" },
            new LyricLine { StartMs = 138160, EndMs = 143850, Text = "Well this one costs a little — but just one time" },
            new LyricLine { StartMs = 143900, EndMs = 149150, Text = "No servers checking in on me" },
            new LyricLine { StartMs = 149200, EndMs = 152110, Text = "No annual renewal down the line" },
            new LyricLine { StartMs = 152160, EndMs = 153670, Text = "Just me, my GPU" },
            new LyricLine { StartMs = 153720, EndMs = 158790, Text = "And ProEssentials doing fine" },
            new LyricLine { StartMs = 158840, EndMs = 161700, Text = "Baby you can render" },
            new LyricLine { StartMs = 161750, EndMs = 164510, Text = "Baby you can render" },
            new LyricLine { StartMs = 164560, EndMs = 169320, Text = "Baby you can render with me-e-e" },
            new LyricLine { StartMs = 169370, EndMs = 172430, Text = "One hundred million points flying free" },
            new LyricLine { StartMs = 172480, EndMs = 176090, Text = "GigaSoft, yeah that's where I wanna be" },
            new LyricLine { StartMs = 176140, EndMs = 178990, Text = "Baby you can render" },
            new LyricLine { StartMs = 179040, EndMs = 183480, Text = "Baby you can render" },
            new LyricLine { StartMs = 183530, EndMs = 187710, Text = "Baby you can render" },
            new LyricLine { StartMs = 187760, EndMs = 190590, Text = "Baby you can render" },
            new LyricLine { StartMs = 190640, EndMs = 193640, Text = "Baby you can render with me" },
        };
        public static LyricLine? GetLine(int currentMs) {
            foreach (var line in Lines)
                if (currentMs >= line.StartMs && currentMs < line.EndMs) return line;
            return null; }
    }
}