using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using System.IO;

namespace TimeLog
{
    class Program
    {
        static int i;
        static void Main(string[] args)
        {
            assert(args.Length == 1);
            string inputPath = args[0];
            assert(inputPath.EndsWith(".log") && File.Exists(inputPath), inputPath);
            string summaryPath = inputPath.Replace(".log", ".summary.txt");
            string detailsPath = inputPath.Replace(".log", ".details.txt");
            run(inputPath, summaryPath, detailsPath);
            //string testPath = inputPath.Replace(".log", ".test.log");
            //run(outputPath, testPath);
            //assert(File.ReadLines(outputPath).SequenceEqual(File.ReadLines(testPath)));
        }

        static void run(string inputPath, string summaryPath, string detailsPath)
        {
            string[] lines = File.ReadAllLines(inputPath);
            assert(lines[0] == ".LOG", lines[0]);
            assert(string.IsNullOrEmpty(lines[1]), lines[1]);
            //string summaryPath = outputPath.Replace(".log", ".txt");
            using (StreamWriter sw = new StreamWriter(detailsPath, false))
            {
                using (StreamWriter sw2 = new StreamWriter(summaryPath, false))
                {
                    Parser parser = new Parser(sw, sw2, DayOfWeek.Monday, true);
                    parser.run(lines);
                }
            }
        }

        class Parser
        {
            readonly StreamWriter sw;
            readonly StreamWriter sw2;
            readonly DayOfWeek? payDay;
            readonly bool monthly;

            DateTime? previousDay;
            DateTime? currentDay;
            DateTime previousDateTime;
            TimeSpan mothlyTimeSpan = TimeSpan.Zero;
            DateTime? firstDate;

            internal Parser(StreamWriter sw, StreamWriter sw2) // monthly
                : this(sw, sw2, null, true)
            {
            }
            internal Parser(StreamWriter sw, StreamWriter sw2, DayOfWeek payDay) // weekly
                : this(sw, sw2, payDay, false)
            {
            }
            internal Parser(StreamWriter sw, StreamWriter sw2, DayOfWeek? payDay, bool monthly)
            {
                this.sw = sw;
                this.sw2 = sw2;
                this.payDay = payDay;
                this.monthly = monthly;

                sw.AutoFlush = true;
                sw2.AutoFlush = true;
            }

            internal void run(string[] lines)
            {
                sw.WriteLine("LOGGED");
                sw.WriteLine();

                // whether we've started a block of times
                DateTime? startedTime = null;
                List<string> activities = new List<string>();
                DateTime? endedTime = null;
                bool started = false;
                bool expectBlank = false;
                bool expectSeparator = false;
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
                        // this could be a total
                        double totalHours;
                        if (double.TryParse(line, out totalHours))
                        {
                            //sw.WriteLine(line);
                            timeSpan = writeTimeSpan(timeSpan, false, false);
                            expectBlank = true;
                            expectSeparator = true;
                            afterTotal = true;
                        }
                        else if (string.IsNullOrEmpty(line))
                        {
                            assert(expectBlank);
                            expectBlank = false;
                            sw.WriteLine();
                            afterBlank = true;
                        }
                        else if (line == "---")
                        {
                            assert(expectSeparator);
                            expectSeparator = false;
                            sw.WriteLine(line);
                            expectBlank = true;
                        }
                        else
                        {
                            // expect this to be a starting time
                            startedTime = parseDateTime(line);
                            started = true;

                            // may be a new day
                            if (!afterTotal && endedPrevious.HasValue && isNewDay(startedTime.Value, endedPrevious.Value))
                            {
                                timeSpan = writeTimeSpan(timeSpan, true, false);
                                afterTotal = true;
                            }
                        }
                        continue;
                    }
                    if (line == "started")
                    {
                        assert(started, "line number " + i);
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
                                assert(activities.Count == 0);
                                startedTime = null;
                                continue;
                            }
                            else
                            {
                                // we have activities
                                assert(activities.Count > 0, "line number " + i);
                                sw.WriteLine(startedTime.Value.ToString(formatDateTime));
                                activities.ForEach(found => sw.WriteLine(found));
                                sw.WriteLine(endedTime.Value.ToString(formatDateTime));

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
                            assert(false);
                            //assert(expectBlank);
                            //expectBlank = false;
                        }
                        sw.WriteLine();
                    }
                    else
                    {
                        if (char.IsDigit(line[0]))
                        {
                            endedTime = parseDateTime(line);
                        }
                        else
                        {
                            activities.Add(line);
                        }
                    }
                }

                if (!afterTotal)
                {
                    writeTimeSpan(timeSpan, true, true);
                }
                else
                {
                    writeMonthlyTotal(previousDay.Value);
                }
            }

            bool spansPayday
            {
                get
                {
                    assert(currentDay.Value.Date > previousDay.Value.Date);
                    if (monthly)
                    {
                        // paid monthly
                        if (currentDay.Value.Month != previousDay.Value.Month)
                            return true;
                        if (!payDay.HasValue)
                            return false;
                    }
                    // else paid weekly on payDay
                    DateTime previousDate = previousDay.Value.Date;
                    if (previousDate == firstDate.Value)
                        return false;
                    DateTime currentDate = currentDay.Value.Date;
                    for (previousDate = previousDate.AddDays(1); previousDate <= currentDate; previousDate = previousDate.AddDays(1))
                    {
                        if (previousDate.DayOfWeek == payDay.Value)
                            return true;
                    }
                    return false;
                }
            }

            TimeSpan writeTimeSpan(TimeSpan timeSpan, bool appendSeparator, bool appendTotal)
            {
                TimeSpan rounded = roundedTimeSpan(timeSpan);
                string s = timeSpanToString(rounded);
                sw.WriteLine(s);
                if (appendSeparator)
                {
                    sw.WriteLine();
                    sw.WriteLine("---");
                    sw.WriteLine();
                }
                if (previousDay.HasValue)
                {
                    if (spansPayday)
                    {
                        writeMonthlyTotal(previousDay.Value);
                    }
                }
                previousDay = currentDay;
                mothlyTimeSpan += rounded;
                string summary = string.Format("{0}\t{1}", currentDay.Value.ToString("yyyy-MM-dd"), s);
                sw2.WriteLine(summary);

                if (appendTotal && mothlyTimeSpan.TotalHours != 0)
                    writeMonthlyTotal(currentDay.Value);

                currentDay = null;
                return timeSpan - rounded;
            }

            void writeMonthlyTotal(DateTime monthNow)
            {
                string s = timeSpanToString(mothlyTimeSpan);
                string total = string.Format("{0} (total)\t{1}", monthNow.ToString("yyyy-MM"), s);
                sw2.WriteLine(total);
                mothlyTimeSpan = TimeSpan.Zero;
            }

            bool isNewDay(DateTime next, DateTime prev)
            {
                assert(next > prev);
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

            static string timeSpanToString(TimeSpan rounded)
            {
                assert(rounded.Seconds == 0);
                assert((rounded.Minutes == 0) || (rounded.Minutes == 30));
                //assert((int)rounded.TotalHours == rounded.TotalHours);
                string s = string.Format((rounded.Minutes == 0) ? "{0}" : "{0}.5", (int)rounded.TotalHours);
                return s;
            }

            static TimeSpan roundedTimeSpan(TimeSpan timeSpan)
            {
                assert(timeSpan.Days == 0);
                if (timeSpan <= TimeSpan.Zero)
                {
                    return TimeSpan.Zero;
                }
                assert((timeSpan.Days == 0) && (timeSpan.Hours <= 18));
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

            DateTime parseDateTime(string line)
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
                assert(previousDateTime <= parsed, line);
                previousDateTime = parsed;
                return parsed;
            }
        }

        static void assert(bool b, string s = null)
        {
            if (!b)
            {
                string line = "Line " + i;
                if (string.IsNullOrEmpty(s))
                    line += " -- " + s;
                throw new Exception(line);
                //if (!string.IsNullOrEmpty(s))
                //    throw new Exception(s);
                //else
                //    throw new Exception();
            }
        }
    }
}
