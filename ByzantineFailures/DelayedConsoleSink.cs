using Serilog.Core;
using Serilog.Events;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ByzantineFailures
{
    /// <summary>
    /// Klasa za periodican ispis logova u konzoli
    /// </summary>
    internal class DelayedConsoleSink : ILogEventSink
    {
        //Konkurentni red u koji se smestaju logovi za ispis
        private static readonly ConcurrentQueue<LogEvent> _logEvents = [];

        //Vremenski period na koji ce se ispisivati logovi
        private static readonly TimeSpan DelayTime = TimeSpan.FromSeconds(1);
        
        /// <summary>
        /// Metoda za periodican ispis logova u toku izvrsavanja simulacije
        /// </summary>
        /// <param name="cancellationToken">Parametar sluzi za prekidanje izvrsavanja Task-a</param>
        /// <returns></returns>
        public static async Task ProcessLogs(CancellationToken cancellationToken)
        {
            //Task se izvrsava sve dok se eksplicitno ne prekine
            while (!cancellationToken.IsCancellationRequested) 
            {
                //Pokusaj dohvatanja dogadjaja iz reda, ako postoji, uklanja se i metoda vraca true,
                //u suprotnom vraca false
                if (_logEvents.TryDequeue(out LogEvent? logEvent))
                {
                    //ispis poruke u konzoli
                    WriteToConsole(logEvent);
                }

                //Cekanje jednu sekundu
                await Task.Delay(DelayTime, cancellationToken);
            }
        }

        /// <summary>
        /// Nakon sto se glavni Task za ispis logova prekine, 
        /// potencijalno je potrebno ispisati zaostale logove iz reda
        /// </summary>
        /// <returns></returns>
        public static async Task ProcessRemainingLogs()
        {
            //Dohvatanje logova dokle god red nije prazan
            while (_logEvents.TryDequeue(out LogEvent? logEvent))
            {
                //Ispis poruke
                WriteToConsole(logEvent);

                //Cekanje jednu sekundu
                await Task.Delay(DelayTime);
            }
        }

        /// <summary>
        /// Metoda za obradu i ispis loga iz reda 
        /// </summary>
        /// <param name="logEvent">Log koji se ispisjue</param>
        private static void WriteToConsole(LogEvent logEvent)
        {
            //poruka u okviru loga
            string formattedMessage = $"{logEvent.RenderMessage()}";

            //promena boje teksta konzole na osnovu nivoa loga
            SetConsoleColor(logEvent.Level);

            //Formatiranje i ispis poruke, prvo se ispisuje timestamp, pa nivo poruke, pa sam sadrzaj poruke
            Console.WriteLine($"{logEvent.Timestamp:HH:mm:ss} [{logEvent.Level}] - {formattedMessage}");

            //vracanje na podrazumevanu boju teksta konzole
            Console.ResetColor();
        }

        /// <summary>
        /// Metoda koja se poziva kada se generise neki log, npr. Logger.Information(...)
        /// </summary>
        /// <param name="logEvent">Log koji se belezi</param>
        public void Emit(LogEvent logEvent)
        {
            //Zato sto je neophodno da se logovi ispisuju na jednu sekundu, ovde se log samo doda u red
            _logEvents.Enqueue(logEvent);
        }

        /// <summary>
        /// Metoda za izbor boje konzole, u zavisnosti od nivoa loga
        /// </summary>
        /// <param name="level">Nivo loga koji se ispisje</param>
        private static void SetConsoleColor(LogEventLevel level)
        {
            Console.ForegroundColor = level switch
            {
                LogEventLevel.Information => ConsoleColor.Green,
                LogEventLevel.Warning => ConsoleColor.Yellow,
                LogEventLevel.Error => ConsoleColor.Red,
                LogEventLevel.Fatal => ConsoleColor.Magenta,
                LogEventLevel.Debug => ConsoleColor.Cyan,
                _ => ConsoleColor.White,
            };
        }
    }
}
