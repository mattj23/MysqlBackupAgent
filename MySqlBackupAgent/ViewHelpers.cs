using System;
using Org.BouncyCastle.Asn1.Cms;

namespace MySqlBackupAgent
{
    public static class ViewHelpers
    {
        public static string ToHumanReadable(this TimeSpan timeSpan)
        {
            string text;
            var absSpan = TimeSpan.FromHours(Math.Abs(timeSpan.TotalHours));
            
            if (absSpan.TotalDays > 2)
            {
                text = $"{(int) absSpan.TotalDays} days";
            }
            else if (absSpan.TotalHours > 2)
            {
                text = $"{(int) absSpan.TotalHours} hours";
            }
            else if (absSpan.TotalMinutes > 2)
            {
                text = $"{(int) absSpan.TotalMinutes} minutes";
            }
            else if (absSpan.TotalSeconds > 10)
            {
                text = $"{(int) absSpan.TotalSeconds} seconds";
            }
            else
            {
                text = "a few seconds";
            }

            return timeSpan > TimeSpan.Zero ? $"In {text}" : $"{text} ago";
        }

        public static string ToHumanReadableSize(this ulong bytes)
        {
            var kb = bytes / 1024.0;
            if (kb < 1024)
            {
                return $"{kb:F1} KiB";
            }

            if (kb < Math.Pow(1024, 2))
            {
                var mb = kb / 1024.0;
                return $"{mb:F1} MiB";
            }

            var gb = kb / Math.Pow(1024, 3);
            return $"{gb:F1} GiB";
        }
    }
}