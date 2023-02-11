using calc_lib;
using calc_lib.Models;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Threading;

namespace calc_lib_tester
{
    class Program
    {
        // This class uses reflection to get methods by name, and invokes them.
        // It also does custom checks for specific cases, such as variable arguments, parsing custom models, etc.
        // Feel free to browse, but this is not considered "basic" and as such is not documented well.
        public static void Main(string[] args)
        {
            // Setting up the stopwatch
            var stopwatch = SetUpStopwatch();

            var calculationsType = typeof(Calculations);
            Console.WriteLine("C# Calculator");
            Console.WriteLine("Input name of calculation followed by arguments, all separated by \",\", or \"exit\" to exit.");
            Console.WriteLine("To run a calculation for a number range, follow the format \"start-end\" (both numbers inclusive.");
            Console.WriteLine("Running more than 15 calculations at a time will disable result printout.");
            Console.WriteLine("To input fractions, follow the format \"x/y\".");

            string input = "";
            do
            {
                try
                {
                    Console.Write("\nEnter calculation: ");
                    input = Console.ReadLine();
                    var inputArray = input.Split(',');
                    var inputParameterStrings = new string[inputArray.Length - 1];
                    Array.ConstrainedCopy(inputArray, 1, inputParameterStrings, 0, inputParameterStrings.Length);

                    var method = calculationsType.GetMethod(inputArray[0]);
                    var methodParamInfo = method.GetParameters();
                    var executions = new List<Func<object>>();
                    if (inputParameterStrings.Length == 1 && inputParameterStrings[0].Contains("-"))
                    {
                        var splitInputRange = inputParameterStrings[0].Split("-");
                        var start = int.Parse(splitInputRange[0]);
                        var end = int.Parse(splitInputRange[1]);

                        Console.Write("Preparing 1 executions...");
                        for (int c = start; c <= end; c++)
                        {
                            var numberString = c.ToString();
                            var paramStrings = new string[] { c.ToString() };
                            executions.Add(() => method.Invoke(null, ParseParameters(methodParamInfo, paramStrings)));
                            var backspaces = new char[(c - 1).ToString().Length + 14];
                            Array.Fill(backspaces, '\b');
                            Console.Write(backspaces);
                            Console.Write(c + " executions...");
                        }
                        Console.WriteLine(" done.");
                    } 
                    else
                        executions.Add(() => method.Invoke(null, ParseParameters(methodParamInfo, inputParameterStrings)));

                    if (executions.Count > 15)
                        Console.Write("Running " + executions.Count + " executions...");

                    var executionTimes = new List<TimeSpan>();
                    foreach (var execution in executions)
                    {
                        stopwatch.Restart();
                        var result = execution.Invoke();
                        stopwatch.Stop();
                        var executionTime = stopwatch.Elapsed;

                        if (executions.Count <= 15)
                        {
                            Console.Write("The answer is: ");
                            if (result is List<int> enumerableResult)
                            {
                                if (enumerableResult.Count > 0)
                                    Console.WriteLine(string.Join(", ", enumerableResult));
                                else
                                    Console.WriteLine("none");
                            }
                            else if (result == null)
                                Console.WriteLine("none");
                            else
                                Console.WriteLine(result);
                            Console.WriteLine("Execution time: " + (executionTime.Ticks * 0.1m) + " microseconds");
                        }
                        executionTimes.Add(executionTime);
                    }

                    if (executions.Count > 15)
                        Console.WriteLine(" done.");

                    if (executionTimes.Count > 1)
                    {
                        var totalExecutionTime = TimeSpan.Zero;
                        foreach (var executionTime in executionTimes)
                            totalExecutionTime += executionTime;
                        var averageExecutionTime = totalExecutionTime / executionTimes.Count;
                        Console.WriteLine("Total execution time: " + (totalExecutionTime.Ticks * 0.0001m) + " milliseconds");
                        Console.WriteLine("Average execution time: " + (averageExecutionTime.Ticks * 0.1m) + " microseconds");
                    }
                } 
                catch (Exception)
                {
                    Console.WriteLine("Invalid input.");
                }
            }
            while (!input.Equals("exit"));
        }

        static Stopwatch SetUpStopwatch()
        {
            Stopwatch stopwatch = new Stopwatch();

            long seed = Environment.TickCount;  // Prevents the JIT Compiler from optimizing FKT calls away
            long result = 0;
            int count = 100000000;

            Process.GetCurrentProcess().ProcessorAffinity = new IntPtr(2);          // Use second core
            Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.High;  // Prevent other processes from interrupting threads
            Thread.CurrentThread.Priority = ThreadPriority.Highest;                 // Prevent other threads from interrupting this thread

            stopwatch.Start();
            while (stopwatch.ElapsedMilliseconds < 1200) // Perform a 1000-1500 ms warmup to stabilize the CPU cache and pipeline.
            {
                result = TestFunction(seed, count);
            }
            stopwatch.Stop();
            stopwatch.Reset();
            Console.WriteLine("Stopwatch seed: " + result); // prevents optimizations (current compilers are too silly to analyze the dataflow that deep, but we never know)

            return stopwatch;
        }

        static long TestFunction(long seed, int count)
        {
            long result = seed;
            for (int i = 0; i < count; ++i)
            {
                result ^= i ^ seed; // Some useless bit operations
            }
            return result;
        }

        static object[] ParseParameters(ParameterInfo[] methodParamInfo, string[] inputParameterStrings)
        {
            var parameters = new List<object>();
            for (int i = 0; i < methodParamInfo.Length; i++)
            {
                if (methodParamInfo[i].GetCustomAttributes(typeof(ParamArrayAttribute), false).Length > 0)
                {
                    var remainingParamStrings = new string[inputParameterStrings.Length - i];
                    Array.ConstrainedCopy(inputParameterStrings, i, remainingParamStrings, 0, remainingParamStrings.Length);
                    parameters.Add(ParseParameter(int.Parse, remainingParamStrings));
                }
                else if (methodParamInfo[i].ParameterType == typeof(DecimalNumber))
                {
                    parameters.Add(ParseParameter((paramString) =>
                    {
                        var splitInputParameterString = inputParameterStrings[0].Split('.');
                        // Using an object initializer
                        return new DecimalNumber()
                        {
                            Left = int.Parse(splitInputParameterString[0]),
                            Right = int.Parse(splitInputParameterString[1])
                        };
                    }, inputParameterStrings[i]));
                    
                }
                else if (methodParamInfo[i].ParameterType == typeof(Fraction))
                    parameters.Add(ParseParameter((paramString) =>
                    {
                        var splitInputParameterString = paramString.Split('/');
                        // Using a constructor
                        return new Fraction(int.Parse(splitInputParameterString[0]), int.Parse(splitInputParameterString[1]));
                    }, inputParameterStrings[i]));
                else if (methodParamInfo[i].ParameterType == typeof(int))
                    parameters.Add(int.Parse(inputParameterStrings[i]));
                else if (methodParamInfo[i].ParameterType == typeof(long))
                    parameters.Add(long.Parse(inputParameterStrings[i]));
                else if (methodParamInfo[i].ParameterType == typeof(float))
                    parameters.Add(float.Parse(inputParameterStrings[i]));
            }
            return parameters.ToArray();
        }

        static T[] ParseParameter<T>(Func<string, T> parser, params string[] paramStrings)
        {
            var output = new List<T>();
            foreach (var paramString in paramStrings)
                output.Add(parser.Invoke(paramString));
            return output.ToArray();
        }
    }
}
