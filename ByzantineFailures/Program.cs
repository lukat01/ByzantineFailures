using Serilog;
using Serilog.Core;
using System;
using System.IO;
using System.Threading;

namespace ByzantineFailures
{
    /// <summary>
    /// Enum sluzi za belezenje tipa simulacije, od starta, ili od checkpoint-a
    /// </summary>
    enum RunType
    {
        Error,
        NewRun,
        FromCheckpoint
    }

    /// <summary>
    /// Klasa u kojoj se nalazi Main metoda
    /// </summary>
    internal class Program
    {
        //Podrazumevana vrednost poruke
        public const int DefaultMessageValue = 128;

        //Indeks glavnog generala
        public const int CommanderIndex = 0;

        //Putanja do foldera sa logovima za pokretanje
        //Za svako izvrsavanje se pravi novi folder na osnovu vremena pokretanja
        //U slucaju da se koristi checkpoint, logovi se nastavljaju
        public static DateTime StartTime { get; private set; } = DateTime.Now;

        //Pocetno vreme izvrsavanja kada je simulacija pokrenuta od checkpoint-a
        public static DateTime CurrentTime { get; private set; } = DateTime.Now;

        //Vremenski interval dosadasnjeg izvrsavanja
        //Sluzi da bi se smanjilo cekanje u slucaju pokretanja od checkpointa
        public static TimeSpan? SpentTime { get; private set; }

        //Putanja do foldera za logove (moze da se promeni, u slucaju da se simulacija pokrece od checkpoint-a)
        public static string LogsFolder { get; private set; } = $"../../../logs/{StartTime:yyyy-MM-dd_HH-mm-ss}";

        //Adresa foldera za checkpoint-e
        public static readonly string CheckpointsFolder = $"../../../checkpoints";

        //Cancelation token source za zaustavljanje taskova generala
        private static readonly CancellationTokenSource cts = new();

        //Flag koji govori da li se radi checkpoint
        private static bool _checkpoint = false;

        //CheckpointReader se kreira u slucaju da se simulacija nastavlja
        public static CheckpointReader? CheckpointReader { get; private set; }

        //Flag koji citaju generali da bi znali zasto je task prekinut
        //(ili je komunikacija zavrsena ili se radi checkpoint)
        public static bool CommunicationCompleted { get; private set; } = false;

        //Lista sa generalima
        public static List<AbstractGeneral> Generals { get; private set; } = [];

        //Broj generala u sistemu
        public static int NumberOfGenerals { get; private set; }

        //Inicijalizacija loggera za konzolu i za fajl
        public static Logger Logger { get; private set; } = new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .WriteTo.Sink(new DelayedConsoleSink())
                .WriteTo.File(path: $"{LogsFolder}/info.txt")
                .CreateLogger();


        /// <summary>
        /// Metoda za obradu argumenata i generisanje zadatih parametara
        /// </summary>
        /// <param name="args">Arumenti Main metode</param>
        /// <returns>N, m, da li je glavni general lojalan, vrednost poruke, lista generala koji nisu lojalni</returns>
        private static (
            RunType runInfo, 
            int N, 
            int m, 
            bool commanderLoyal, 
            int messageValue, 
            List<int> unloyalGenerals) 
            InformationInitialization(string[] args)
        {
            if (args.Length == 1)
            {
                //Ako je broj argumenata 1, u pitanju je pokretanje od checkpoint-a
                //Potrebno je dohvatiti informacije iz fajla
                return GenerateParametersFromCheckpoint(args[0]);
            }
            else if (args.Length == 3) 
            {
                //Ako je broj argumenata 3, simulacija se pokrece od nule, parametri su argumenti komandne linije
                //Kreiranje foldera za logove
                Directory.CreateDirectory(LogsFolder);
                return GenerateParametersFromArguments(args);
            }
            else
            {
                //U ostalim slujcajevima, greska
                Logger.Error("Wrong number of arguments, three arguments required");
                return (RunType.Error, default, default, default, default, []);
            }
        }

        /// <summary>
        /// Metoda za generisanje osnovnih informacija, u slucaju kada se simulacija pokrece od nule
        /// </summary>
        /// <param name="args">Argumenti komandne linije</param>
        /// <returns>N, m, da li je glavni general lojalan, vrednost poruke, lista generala koji nisu lojalni</returns>
        private static (
            RunType runInfo, 
            int N, 
            int m, 
            bool commanderLoyal,
            int messageValue, 
            List<int> unloyalGenerals) 
            GenerateParametersFromArguments(string[] args)
        {
            //Parsiranje argumenata
            int N = int.Parse(args[0]);
            int m = int.Parse(args[1]);
            bool commandingGeneralLoyal = args[2] == "1";

            //Nasumicno generisanje vrednosti poruke
            int messageValue = new Random().Next(2 * DefaultMessageValue);

            //Provera uslova za funkcionisanje algoritma
            if (N < m + 2)
            {
                Logger.Error("Algorithm doesn't work for these values of N and m");
                return (RunType.Error, default, default, default, default, []);
            }

            //Logovanje osnovnih informacija o izvrsavanju
            Logger.Verbose($"Number of generals: {N}");
            Logger.Verbose($"Number of unloyal generals: {m}");
            Logger.Verbose($"Commanding general is loyal: {commandingGeneralLoyal}");
            Logger.Information($"Default message value: {DefaultMessageValue}");
            Logger.Information($"Message value: {messageValue}");

            //Lista sa indeksima neispravnih generala
            List<int> unloyalGenerals = [];

            //Provera da li je potrebno odabrati neispravne generale
            if (m > 0)
            {
                //Biranje m neispravnih generala
                unloyalGenerals = Enumerable.Range(1, N - 1)
                                                        .OrderBy(x => new Random().Next())
                                                        //U m ulazi i glavni general ako je on neispravan
                                                        .Take(commandingGeneralLoyal ? m : m - 1)
                                                        .ToList();
                string unloyalGeneralsAppended = "Unloyal generals are: ";
                unloyalGeneralsAppended += string.Join(", ", (commandingGeneralLoyal ? unloyalGenerals : unloyalGenerals
                                                                                        .Append(CommanderIndex))
                                                                                        .OrderBy(x => x));
                //Logovanje nelojalnih generala
                Logger.Verbose(unloyalGeneralsAppended);
            }
            else
            {
                //Logovanje informacije da su svi generali lojalni
                Logger.Verbose("All generals are loyal");
            }

            return (RunType.NewRun, N, m, commandingGeneralLoyal, messageValue, unloyalGenerals);
        }

        /// <summary>
        /// Dohvatanje osnovnih informacija na osnovu chekpoint fajla
        /// </summary>
        /// <param name="fileName"></param>
        /// <returns></returns>
        private static (
            RunType runInfo,
            int N,
            int m,
            bool commanderLoyal,
            int messageValue,
            List<int> unloyalGenerals)
            GenerateParametersFromCheckpoint(string fileName)
        {
            try
            {
                //Inijcijalizacija objekta za citanje checkpoint fajla
                CheckpointReader = new(fileName);

                //Dohvatanje pocetnog i trenutnog vremena simulacije
                (StartTime, SpentTime) = CheckpointReader.LoadStartAndCurrentTime();

                //Generisanje novog logger-a, koji ce nastaviti logove u isti fajl kao i prethodno pokretanje
                LogsFolder = $"../../../logs/{StartTime:yyyy-MM-dd_HH-mm-ss}";
                Logger = new LoggerConfiguration()
                    .MinimumLevel.Verbose()
                    .WriteTo.Sink(new DelayedConsoleSink())
                    .WriteTo.File(path: $"{LogsFolder}/info.txt")
                    .CreateLogger();

                Logger.Information($"Context restoration from checkpoint, start time: {StartTime:yyyy-MM-dd_HH-mm-ss}");

                //Dohvatanje osnovnih informacija
                return CheckpointReader.LoadGeneralInformation();
            }
            catch (Exception)
            {
                //U slucaju da fajl nije u dobrom formatu, vraca se status greske
                return (RunType.Error, default, default, default, default, []);
            }
        }

        /// <summary>
        /// Task za ispisivanje preostalih logova
        /// Poziva se na kraju izvrsavanja
        /// </summary>
        /// <param name="cts1">Cancelation token source za logging task</param>
        /// <param name="loggingTask">Task za periodicno logovanje</param>
        /// <returns></returns>
        private static async Task FinalizationAsync(CancellationTokenSource cts1, Task loggingTask)
        {
            //Preostalo je da se prekine izvrsavanje procesa za logovanje
            try
            {
                //Prekidanje procesa
                cts1.Cancel();
                await loggingTask;
            }
            catch (TaskCanceledException)
            {
                //Ispis potencijalno zaostalih logova
                await DelayedConsoleSink.ProcessRemainingLogs();
            }
            finally
            {
                cts1.Dispose();
            }

            //Ciscenje i zatvaranje logova
            Logger.Dispose();
        }

        /// <summary>
        /// Metoda za spavanje sa nasumicno vreme
        /// Koristi se prilikom komunikacije generala u metodi Communication
        /// </summary>
        public static void RandomSleep()
        {
            int milliseconds = new Random().Next(500, 1000);
            Thread.Sleep(milliseconds);
        }

        /// <summary>
        /// Metoda koja reaguje na dogadjaj Ctrl+C
        /// Tada se zahteva prekid simulacije
        /// Pre zavrsavanja simulacije, cuva se kontekst, odnosno generise se checkpoint
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        static void OnCancelKeyPress(object sender, ConsoleCancelEventArgs e)
        {
            e.Cancel = true;
            _checkpoint = true;
            Logger.Information("Simulation is interrupted");
            cts.Cancel();
            Thread.Sleep(3000);
        }

        /// <summary>
        /// Glavna metoda simulacije
        /// </summary>
        /// <param name="args">
        /// Argumenti Main metode su redom
        /// N - Broj generala
        /// m - Broj neispravnih cvorova
        /// commandingGeneralLoyal - 
        ///     1 ako je glavni general lojalan
        ///     0 u suprotnom
        /// </param>
        /// <returns></returns>
        static async Task Main(string[] args)
        {
            //Kreiranje tokena za zaustavljanje procesa za periodican ispis logova
            using CancellationTokenSource cts1 = new();
            CancellationToken token1 = cts1.Token;

            //Pokretanje Task-a za periodican ispis logova
            Task loggingTask = Task.Run(() => DelayedConsoleSink.ProcessLogs(token1));

            Console.CancelKeyPress += new ConsoleCancelEventHandler(OnCancelKeyPress!);

            (RunType runInfo, NumberOfGenerals, int m, bool commandingGeneralLoyal, int messageValue, 
                List<int> unloyalGenerals) = InformationInitialization(args);

            //Greska prilikom generisanja osnovnih parametara, kraj simulacije
            if (runInfo == RunType.Error)
            {
                await FinalizationAsync(cts1, loggingTask);
                return;
            }

            //Inicijalizacija glavnog generala
            Generals = new(NumberOfGenerals)
            {
                new CommandingGeneral(commandingGeneralLoyal, CommanderIndex, messageValue)
            };

            //Kreiranje objekata za ostale generale
            for (int i = 1; i < NumberOfGenerals; i++)
            {
                //Svakom generalu se u konstruktoru prosledjuje da li su lojalni i koji je njihov indeks
                Generals.Add(new General(!unloyalGenerals.Contains(i), i));
            }


            if (runInfo == RunType.NewRun) 
            {
                //Pokretanje od pocetka

                //Kraj inicijalizacije generala
                Logger.Verbose("Generals initialized");

                //Generisnje i raspodela kljuceva RSA seme
                CertificationAuthority.Instance.GenerateKeyPairs(NumberOfGenerals);
                Logger.Verbose("Keys generated and distributed");
            }
            else
            {
                Logger.Verbose("Generals created");

                //Ucitavanje i raspodela kljuceva RSA seme na osnovu checkpoint-a
                CertificationAuthority.Instance.LoadKeysFromCheckpoint(CheckpointReader!);
            }

            if (runInfo == RunType.FromCheckpoint) 
            {
                //Dohvatanje prethodnih poruka
                CheckpointReader!.LoadMessages();

                //Smestanje poruka u odgovarajuce liste i redove kod generala
                CheckpointReader.LoadGenerals();
            }

            //Za laksi prekid izvrsavanja cekamo da se ispisu svi logovi za inicijalizaciju
            await DelayedConsoleSink.ProcessRemainingLogs();

            //Pocetak komunikacije
            Logger.Information("Communication begins");

            //Za svakog od generala se kreira Task u kome se izvrsava Communication metoda tog generala
            Task[] tasks = new Task[NumberOfGenerals];

            //Da bi generali znali kada je komunikacija gotova, potrebno je da se eksplicitno prekine izvrsavanje sa tokenom
            CancellationToken token = cts.Token;

            //Inicijalizacija taskova za generale
            for (int i = runInfo == RunType.NewRun ? 0 : 1; i < NumberOfGenerals; i++)
            {
                int index = i;
                tasks[index] = Task.Run(() => Generals[index].Communication(token));
            }

            try
            {
                if (runInfo == RunType.NewRun)
                {
                    //Ako se simulacija pokrece od pocetka ceka se fiksno vreme
                    await Task.Delay(10000 * NumberOfGenerals ^ 2, token);
                }
                else
                {
                    //Ako se pokrece od checkpointa, task glavnog generala nije inicijalizovan
                    //Da bi se izbegla greska, potrebno je inicijalizovati bilo kakav task u nizu
                    tasks[0] = Task.Run(() => { });
                    await Task.Delay((10000 * NumberOfGenerals ^ 2) - SpentTime!.Value.Milliseconds, token);
                }
                CommunicationCompleted = true;
            }
            catch (TaskCanceledException)
            {

            }

            //Prekidanje taskova
            cts.Cancel();
            //Cekanje da se svi task-ovi zavrse
            try
            {
                Task.WaitAll(tasks);
            }
            catch (OperationCanceledException)
            {
                
            }
            finally
            {
                cts.Dispose();
            }

            if (_checkpoint) 
            {
                //Ako je sa Ctrl+C prekinuto izvrsavanje, potrebno je generisati checkpoint

                //using kljucna rec se koristi da bi se automatski pozvala Dispose metoda za CheckpointWriter
                using CheckpointWriter checkpointWriter = new($"{StartTime:yyyy-MM-dd_HH-mm-ss}.txt", m, 
                    commandingGeneralLoyal, messageValue, unloyalGenerals);
                Logger.Information("Checkpoint is being written");
                checkpointWriter.StoreCheckpoint(DateTime.Now);
                Logger.Information("Checkpoint is completed");
            }
            else
            {
                //Kraj komunikacije
                Logger.Information("Communication completed");
            }

            //Ispisivanje preostalih logova
            await FinalizationAsync(cts1, loggingTask);
        }
    }
}