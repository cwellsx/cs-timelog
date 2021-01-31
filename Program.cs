using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using System.IO;
using System.Configuration;
using System.Globalization;

namespace TimeLog
{
    class Program
    {
        static class Settings
        {
            static Settings()
            {
                var settings = ConfigurationManager.AppSettings;
                Monthly = bool.Parse(settings["Monthly"]);
                var payDay = settings["Weekly"];
                if (payDay != null)
                {
                    Weekly = (DayOfWeek)Enum.Parse(typeof(DayOfWeek), payDay);
                }
                EveryWeekDay = bool.Parse(settings["EveryWeekDay"]);
                ShowDayOfWeek = bool.Parse(settings["ShowDayOfWeek"]);
            }
            internal static readonly bool Monthly;
            internal static readonly DayOfWeek? Weekly;
            internal static readonly bool EveryWeekDay;
            internal static readonly bool ShowDayOfWeek;
        }

        static int i;

        static void Main(string[] args)
        {
            try
            {
                Assert(args.Length == 1);
                string inputPath = args[0];
                Assert(inputPath.EndsWith(".log") && File.Exists(inputPath));
                string summaryPath = inputPath.Replace(".log", ".summary.txt");
                string detailsPath = inputPath.Replace(".log", ".details.txt");
                Run(inputPath, summaryPath, detailsPath);
            }
            catch (Exception)
            {
                Console.WriteLine("Exception on line {0}", i);
            }
        }

        static void Run(string inputPath, string summaryPath, string detailsPath)
        {
            string[] lines = File.ReadAllLines(inputPath);
            Assert(lines[0] == ".LOG");
            Assert(string.IsNullOrEmpty(lines[1]));
            using (StreamWriter sw = new StreamWriter(detailsPath, false))
            {
                using (StreamWriter sw2 = new StreamWriter(summaryPath, false))
                {
                    Parser parser = new Parser(sw, sw2);
                    parser.Run(lines);
                }
            }
        }

        class Parser
        {
            readonly StreamWriter swDetails;
            readonly StreamWriter swSummary;

            DateTime? previousDay;
            DateTime? currentDay;
            DateTime previousDateTime;
            TimeSpan monthlyTimeSpan = TimeSpan.Zero;
            TimeSpan weeklyTimeSpan = TimeSpan.Zero;
            DateTime? firstDate;
            bool isPartialWeek;

            internal Parser(StreamWriter swDetails, StreamWriter swSummary)
            {
                this.swDetails = swDetails;
                this.swSummary = swSummary;

                swDetails.AutoFlush = true;
                swSummary.AutoFlush = true;
            }

            internal void Run(string[] lines)
            {
                swDetails.WriteLine("LOGGED");
                swDetails.WriteLine();

                // whether we've started a block of times
                DateTime? startedTime = null;
                List<string> activities = new List<string>();
                DateTime? endedTime = null;
                bool started = false;
                bool expectBlank = false;
                bool afterBlank = false;
                bool afterTotal = false;

                TimeSpan timeSpan = new TimeSpan();
                DateTime? endedPrevious = null;

                for (i = 2; i < lines.Length; ++i)
                {
                    string line = lines[i];

                    // ignore extra blanks
                    if (afterBlank)
                    {
                        if (string.IsNullOrEmpty(line))
                            continue;
                    }
                    afterBlank = false;

                    if (!startedTime.HasValue)
                    {
                        if (string.IsNullOrEmpty(line))
                        {
                            Assert(expectBlank);
                            expectBlank = false;
                            swDetails.WriteLine();
                            afterBlank = true;
                        }
                        else
                        {
                            // expect this to be a starting time
                            startedTime = ParseDateTime(line);
                            started = true;

                            // may be a new day
                            if (!afterTotal && endedPrevious.HasValue && IsNewDay(startedTime.Value, endedPrevious.Value))
                            {
                                timeSpan = WriteDailyTotal(timeSpan, false);
                                afterTotal = true;
                            }
                        }
                        continue;
                    }
                    if (line == "started")
                    {
                        Assert(started);
                        started = false;
                        continue;
                    }
                    started = false;
                    if (string.IsNullOrEmpty(line))
                    {
                        afterBlank = true;
                        if (startedTime.HasValue)
                        {
                            if (!endedTime.HasValue)
                            {
                                Assert(activities.Count == 0);
                                startedTime = null;
                                continue;
                            }
                            else
                            {
                                // we have activities
                                Assert(activities.Count > 0);
                                swDetails.WriteLine(startedTime.Value.ToString(formatDateTime));
                                activities.ForEach(found => swDetails.WriteLine(found));
                                swDetails.WriteLine(endedTime.Value.ToString(formatDateTime));

                                endedPrevious = endedTime;
                                TimeSpan newTimeSpan = endedTime.Value - startedTime.Value;
                                timeSpan += newTimeSpan;
                                afterTotal = false;
                                if (!currentDay.HasValue)
                                {
                                    currentDay = startedTime;
                                    if (!firstDate.HasValue)
                                        firstDate = currentDay.Value.Date;
                                }
                            }
                            startedTime = null;
                            endedTime = null;
                            activities.Clear();
                        }
                        else
                        {
                            Assert(false);
                            //assert(expectBlank);
                            //expectBlank = false;
                        }
                        swDetails.WriteLine();
                    }
                    else
                    {
                        if (char.IsDigit(line[0]))
                        {
                            endedTime = ParseDateTime(line);
                        }
                        else
                        {
                            activities.Add(line);
                        }
                    }
                }

                if (!afterTotal)
                {
                    WriteDailyTotal(timeSpan, true);
                }
                else
                {
                    WriteMonthlyTotal(previousDay.Value);
                }
            }

            bool IsNewMonth
            {
                get
                {
                    Assert(currentDay.Value.Date > previousDay.Value.Date);
                    return Settings.Monthly && (currentDay.Value.Month != previousDay.Value.Month);
                }
            }

            bool IsNewWeek
            {
                get
                {
                    Assert(currentDay.Value.Date > previousDay.Value.Date);
                    if (!Settings.Weekly.HasValue)
                        return false;
                    DateTime previousDate = previousDay.Value.Date;
                    if (previousDate == firstDate.Value)
                        return false;
                    DateTime currentDate = currentDay.Value.Date;
                    for (previousDate = previousDate.AddDays(1); previousDate <= currentDate; previousDate = previousDate.AddDays(1))
                    {
                        if (previousDate.DayOfWeek == Settings.Weekly.Value)
                            return true;
                    }
                    return false;
                }
            }

            TimeSpan WriteDailyTotal(TimeSpan timeSpan, bool isEndOfFile)
            {
                TimeSpan rounded = RoundedTimeSpan(timeSpan);
                string s = TimeSpanToString(rounded);

                swDetails.WriteLine(s);
                swDetails.WriteLine();
                swDetails.WriteLine("---");
                swDetails.WriteLine();

                if (Settings.EveryWeekDay && previousDay.HasValue)
                {
                    var saved = currentDay.Value;
                    for (currentDay = previousDay.Value.AddDays(1); saved.Date != currentDay.Value.Date; currentDay = previousDay.Value.AddDays(1))
                    {
                        WriteTotals();
                        if (currentDay.Value.DayOfWeek != DayOfWeek.Saturday && currentDay.Value.DayOfWeek != DayOfWeek.Sunday)
                        {
                            WriteSummary("0");
                        }
                        previousDay = currentDay;
                    }
                }

                if (previousDay.HasValue)
                {
                    WriteTotals();
                }
                weeklyTimeSpan += rounded;
                monthlyTimeSpan += rounded;

                void WriteSummary(string hours)
                {
                    string summary = Settings.ShowDayOfWeek
                        ? string.Format("{0}\t{1}\t{2}", currentDay.Value.DayOfWeek.ToString().Substring(0, 1), currentDay.Value.ToString("yyyy-MM-dd"), hours)
                        : string.Format("{0}\t{1}", currentDay.Value.ToString("yyyy-MM-dd"), hours);
                    swSummary.WriteLine(summary);
                }

                void WriteTotals()
                {
                    if (IsNewWeek)
                    {
                        WriteWeeklyTotal(previousDay.Value, true);
                    }
                    if (IsNewMonth)
                    {
                        WriteMonthlyTotal(previousDay.Value);
                    }
                }

                WriteSummary(s);

                if (isEndOfFile && monthlyTimeSpan.TotalHours != 0)
                    WriteMonthlyTotal(currentDay.Value);

                previousDay = currentDay;
                currentDay = null;
                return timeSpan - rounded;
            }

            void WriteMonthlyTotal(DateTime monthNow)
            {
                if (Settings.Weekly.HasValue && "0" != TimeSpanToString(weeklyTimeSpan))
                    WriteWeeklyTotal(monthNow, false);
                string s = TimeSpanToString(monthlyTimeSpan);
                string total = string.Format("{0} (total)\t{1}", monthNow.ToString("yyyy-MM"), s);
                swSummary.WriteLine(total);
                swSummary.WriteLine();
                monthlyTimeSpan = TimeSpan.Zero;
            }

            void WriteWeeklyTotal(DateTime weekNow, bool isEndOfWeek)
            {
                string s = TimeSpanToString(weeklyTimeSpan);
                string total = string.Format("Week-{0} ({1})\t{2}", GetIso8601WeekOfYear(weekNow).ToString("D2"), !isEndOfWeek || isPartialWeek ? "part" : "total", s);
                isPartialWeek = !isEndOfWeek;
                swSummary.WriteLine(total);
                weeklyTimeSpan = TimeSpan.Zero;
                if (!Settings.Monthly)
                    swSummary.WriteLine();
            }

            // https://stackoverflow.com/questions/11154673/get-the-correct-week-number-of-a-given-date
            public static int GetIso8601WeekOfYear(DateTime time)
            {
                // This presumes that weeks start with Monday.
                // Week 1 is the 1st week of the year with a Thursday in it.

                // Seriously cheat.  If its Monday, Tuesday or Wednesday, then it'll 
                // be the same week# as whatever Thursday, Friday or Saturday are,
                // and we always get those right
                DayOfWeek day = CultureInfo.InvariantCulture.Calendar.GetDayOfWeek(time);
                if (day >= DayOfWeek.Monday && day <= DayOfWeek.Wednesday)
                {
                    time = time.AddDays(3);
                }

                // Return the week of our adjusted day
                return CultureInfo.InvariantCulture.Calendar.GetWeekOfYear(time, CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday);
            }

            bool IsNewDay(DateTime next, DateTime prev)
            {
                Assert(next > prev);
                if (next.Year > prev.Year)
                    return true;
                if ((next.DayOfYear - prev.DayOfYear) >= 2)
                    return true;
                if (((next.DayOfYear - prev.DayOfYear) == 1) && (next.Hour >= 0))
                    return true;
                //if (((next.DayOfYear - prev.DayOfYear) == 0) && (prev.Hour < 4) && (next.Hour >= 4) &&
                if (((next.DayOfYear - prev.DayOfYear) == 0) && (next.Hour >= 4) &&
                    currentDay.HasValue && (currentDay.Value < next.Date))
                    return true;
                return false;
            }

            static string TimeSpanToString(TimeSpan rounded)
            {
                Assert(rounded.Seconds == 0);
                Assert((rounded.Minutes == 0) || (rounded.Minutes == 30));
                //assert((int)rounded.TotalHours == rounded.TotalHours);
                string s = string.Format((rounded.Minutes == 0) ? "{0}" : "{0}.5", (int)rounded.TotalHours);
                return s;
            }

            static TimeSpan RoundedTimeSpan(TimeSpan timeSpan)
            {
                Assert(timeSpan.Days == 0);
                if (timeSpan <= TimeSpan.Zero)
                {
                    return TimeSpan.Zero;
                }
                Assert((timeSpan.Days == 0) && (timeSpan.Hours <= 18));
                TimeSpan t15 = new TimeSpan(timeSpan.Hours, 15, 0);
                TimeSpan t45 = new TimeSpan(timeSpan.Hours, 45, 0);
                if (timeSpan <= t15)
                {
                    return new TimeSpan(timeSpan.Hours, 0, 0);
                }
                if (timeSpan <= t45)
                {
                    return new TimeSpan(timeSpan.Hours, 30, 0);
                }
                return new TimeSpan(timeSpan.Hours + 1, 0, 0);
            }

            static string formatDateTime = "HH:mm yyyy-MM-dd";
            static IFormatProvider provider = null;

            DateTime ParseDateTime(string line)
            {
                DateTime parsed;
                // handle "21:08 2016-07-26" or "22:23 26/07/2016"
                if (line.Contains('-'))
                {
                    parsed = DateTime.ParseExact(line, formatDateTime, provider);
                }
                else
                {
                    parsed = DateTime.ParseExact(line, "HH:mm dd/MM/yyyy", provider);
                }
                Assert(previousDateTime <= parsed);
                previousDateTime = parsed;
                return parsed;
            }
        }

        static void Assert(bool b)
        {
            if (!b)
            {
                throw new Exception();
            }
        }
    }
}
