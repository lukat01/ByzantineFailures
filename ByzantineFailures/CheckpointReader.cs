using Serilog.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace ByzantineFailures
{
    /// <summary>
    /// Klasa za citanje checkpoint-a i restauraciju konteksta izvrsavanja
    /// </summary>
    /// <param name="fileName">Naziv fajla u kome se nalazi checkpoint</param>
    internal class CheckpointReader(string fileName) : IDisposable
    {
        //StreamReader za fajl u kome se nalazi checkpoint
        private readonly StreamReader _reader = new($"{Program.CheckpointsFolder}/{fileName}");

        /// <summary>
        /// Metoda za citanje pocetnog i trenutnog vremena simulacije
        /// </summary>
        /// <returns>Pocetno i trentuno vreme simulacije</returns>
        public (DateTime, TimeSpan) LoadStartAndCurrentTime()
        {
            //U prva dva reda fajla se nalaze vremena
            DateTime startTime = DateTimeOffset.Parse(_reader.ReadLine()!).DateTime;
            TimeSpan spentTime = TimeSpan.FromMilliseconds(long.Parse(_reader.ReadLine()!));
            return (startTime, spentTime);
        }

        /// <summary>
        /// Metoda za citanje osnovnih informacija o simulaciji
        /// </summary>
        /// <returns>N, m, da li je glavni general lojalan, vrednost poruke, lista generala koji nisu lojalni</returns>
        public (
            RunType runInfo,
            int N,
            int m,
            bool commanderLoyal,
            int messageValue,
            List<int> unloyalGenerals)
            LoadGeneralInformation()
        {
            //U prvom redu se nalaze N, m, indikator o ispravnosti glavnog generala i vrednost poruke
            Program.Logger.Verbose("Loading simulation information");
            List<int> firstRow = _reader.ReadLine()!.Split(',').ToList().Select(int.Parse).ToList();
            string? secondLine = _reader.ReadLine();

            //U drugom redu se nalaze indeksi generala koji nisu lojalni
            List<int> secondRow = (secondLine is null || secondLine.Trim() == string.Empty) ? [] :
                secondLine.Trim().Split(',').ToList().Select(int.Parse).ToList();

            _reader.ReadLine();
            Program.Logger.Verbose("Simulation information restored");

            return (RunType.FromCheckpoint, firstRow[0], firstRow[1], firstRow[2] == 1, firstRow[3], secondRow);
        }

        /// <summary>
        /// Metoda za ucitavanje RSA parametara
        /// </summary>
        /// <returns>Dve liste, privatnih i javnih RSA parametara</returns>
        public (List<RSAParameters> privateKeys, List<RSAParameters> publicKeys) LoadRSAParameters() 
        {
            Program.Logger.Verbose("Loading RSA parameters");
            List<RSAParameters> privateKeys = new(Program.NumberOfGenerals);
            List<RSAParameters> publicKeys = new(Program.NumberOfGenerals);
            for (int i = 0; i < Program.NumberOfGenerals; i++) 
            {
                (RSAParameters privateKey, RSAParameters publicKey) = DeserializeRSAParameters();
                privateKeys.Add(privateKey);
                publicKeys.Add(publicKey);
            }
            _reader.ReadLine();
            Program.Logger.Verbose("RSA parameters restored");
            return (privateKeys, publicKeys);
        }

        /// <summary>
        /// Metoda za dohvatanje prethodnih poruka
        /// </summary>
        public void LoadMessages()
        {
            Program.Logger.Verbose("Loading previous messages");
            int id = 0;
            while (_reader.Peek() >= 0) 
            {
                //Poruka je sacuvana na sledeci nacin:
                //Prvi red: Id
                //Drugi red: Data (ceo sadrzaj)
                //Treci red: lista primalaca
                //Na osnovu sadrzaja se moze utvrditi ko je poslao poruku (na osnovu sekvence potpisa)
                id = int.Parse(_reader.ReadLine()!);
                string data = _reader.ReadLine()!;
                string? recipientsString = _reader.ReadLine();
                HashSet<int> recipients = (recipientsString is null || recipientsString.Trim() == string.Empty) ? [] :
                    recipientsString.Split(',').Select(int.Parse).ToHashSet();
                _ = new Message(id, data, recipients);
            }

            //Postavljanje poslednjeg statickog id, za generisanje novih poruka
            Message.SetStaticId(id);
            Program.Logger.Verbose("Previous messages restored");
        }

        /// <summary>
        /// Metoda za dohvatanje RSA parametara za jednog generala
        /// </summary>
        /// <returns>Par privatnih i javnih RSA parametara</returns>
        private (RSAParameters privateParameters, RSAParameters publicParameters) DeserializeRSAParameters()
        {
            //U fajlu se nalaze parovi RSA parametara svakog generala redom
            //U svakom paru, prvo su upisani privatni parametri

            //Citanje privatnih parametara
            RSAParameters privateParameters = new()
            {
                Modulus = Convert.FromBase64String(_reader.ReadLine()!.Split(':')[1].Trim()),
                Exponent = Convert.FromBase64String(_reader.ReadLine()!.Split(':')[1].Trim()),
                D = Convert.FromBase64String(_reader.ReadLine()!.Split(':')[1].Trim()),
                P = Convert.FromBase64String(_reader.ReadLine()!.Split(':')[1].Trim()),
                Q = Convert.FromBase64String(_reader.ReadLine()!.Split(':')[1].Trim()),
                DP = Convert.FromBase64String(_reader.ReadLine()!.Split(':')[1].Trim()),
                DQ = Convert.FromBase64String(_reader.ReadLine()!.Split(':')[1].Trim()),
                InverseQ = Convert.FromBase64String(_reader.ReadLine()!.Split(':')[1].Trim())
            };

            //CItanje javnih parametara
            RSAParameters publicParameters = new()
            {
                Modulus = Convert.FromBase64String(_reader.ReadLine()!.Split(':')[1].Trim()),
                Exponent = Convert.FromBase64String(_reader.ReadLine()!.Split(':')[1].Trim())
            };

            return (privateParameters, publicParameters);
        }

        /// <summary>
        /// Metoda za smestanje svih prethodnih poruka u listu poslatih, listu primljenih i red novih primljenih poruka
        /// Kod svih generala
        /// </summary>
        public static void LoadGenerals()
        {
            Program.Logger.Verbose("Loading generals' lists and queues with restored messages");

            //Svaka poruka moze biti u najvise jednoj kolekciji na nivou jednog generala:
            //U listi poslatih
            //U listi primljenih - prepostavka: poruka je obradjena i poslata dalje, ili je u redu (nema izmedju)
            //U redu novopristiglih poruka
            //Ili jos uvek nigde
            foreach (Message message in Message.AllMessages) 
            {
                (_, int value, _, int[] sequence) = Message.CheckAndProcessMessage(message);

                //Poslednji u listi potpisnika je onaj koji ju je poslao
                int sender = sequence[^1];

                //Prolazi se kroz listu generala
                for (int i = 0; i < Program.NumberOfGenerals; i++) 
                {
                    //Ako je indeks generala indeks posiljaoca, poruka se dodaje u listu poslatih
                    if (i == sender)
                    {
                        Program.Generals[i].AddSentMessage(message, value);
                    }
                    //Ako se indeks generala nalazi u listi primalaca, poruka se dodaje u listu primljenih
                    else if (message.Recipients.Contains(i))
                    {
                        Program.Generals[i].AddReceivedMessage(message, value);
                    }
                    //Ako se indeks generala ne nalazi u sekvenci potpisnika, ni u listi primalaca
                    //Poruka se smesta u red novopristiglih poruka
                    else if (!sequence.Contains(i)) 
                    {
                        Program.Generals[i].InsertMessage(message);
                    }
                }
            }
            Program.Logger.Verbose("Messages loaded");
        }

        /// <summary>
        /// Metoda za oslobadjanje StreamReader objekta
        /// Neophodna zato sto se StreamReader kreira kao polja, pa nije moguce koristiti using
        /// </summary>
        public void Dispose()
        {
            _reader.Dispose();
        }
    }
}
