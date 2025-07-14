using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ByzantineFailures
{
    /// <summary>
    /// Klasa koja se koristi za pisanje checkpoint-a
    /// </summary>
    /// <param name="fileName">Ime fajla u kome se pise checkpoint</param>
    /// <param name="m">Broj generala koji nisu lojalni</param>
    /// <param name="commanderLoyal">Indikator da li je glavni general lojalan</param>
    /// <param name="messageValue">Vrednost poruke</param>
    /// <param name="unloyalGenerals">Niz indeksa generala koji nisu lojalni</param>
    internal class CheckpointWriter
        (string fileName, int m, bool commanderLoyal, int messageValue, List<int> unloyalGenerals) 
        : IDisposable
    {
        //StreamWriter za fajl u kome se cuva checkpoint
        //Ocekuje se sledeci format za fileName: StartTime:yyyy-MM-dd_HH-mm-ss
        //Ovo omogucava da postoji vise checkpoint-a za isto pokretanje simulacije
        private readonly StreamWriter _writer = CreateWriter(fileName);

        //Broj nelojalnih generala
        private readonly int _m = m;

        //Indikator da li je glavni general lojalan
        private readonly bool _commanderLoyal = commanderLoyal;

        //Vrednost poruke
        private readonly int _messageValue = messageValue;

        //Niz indeksa generala koji nisu lojalni
        private readonly List<int> _unloyalGenerals = unloyalGenerals;


        /// <summary>
        /// Staticka metoda za otvaranje toka za upis u fajl
        /// </summary>
        /// <param name="fileName">Naziv fajla</param>
        /// <returns></returns>
        private static StreamWriter CreateWriter(string fileName)
        {
            int fileIndex = 1;
            string newFilePath = $"{Program.CheckpointsFolder}/{fileName}";
            string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(newFilePath);
            string extension = Path.GetExtension(newFilePath);
            string directory = Path.GetDirectoryName(newFilePath) ?? string.Empty;

            //Metoda funkcionise tako sto proverava da li vec postoji zahtevani fajl
            //Ako je to slucaj dodaje se fileIndex i inkrementira, sve dok se ne pronadje naziv koji ne postoji
            while (File.Exists(newFilePath))
            {
                newFilePath = Path.Combine(directory, $"{fileNameWithoutExtension}{fileIndex}{extension}");
                fileIndex++;
            }
            return new(newFilePath);
        }

        /// <summary>
        /// Metoda za oslobadjanje StreamWriter objekta
        /// Neophodna zato sto se StreamWriter kreira kao polja, pa nije moguce koristiti using
        /// </summary>
        public void Dispose()
        {
            _writer.Dispose();
        }

        /// <summary>
        /// Metoda koja cuva kontekst izvrsavanja u fajl
        /// </summary>
        /// <param name="nowTime">
        /// Trenutno vreme u toku izvrsanjava
        /// Koristi se da bi se prilikom novog pokretanja smanjilo cekanje na sve task-ove da se zavrse
        /// </param>
        public void StoreCheckpoint(DateTime nowTime)
        {
            //Upis osnovnih informacija o simulaciji
            Program.Logger.Verbose("Writing simulation information");
            _writer.WriteLine($"{Program.StartTime:O}");

            //Proteklo vreme se racuna na sledeci nacin:
            //Racuna se razlika izmedju nowTime - Program.CurrentTime
            //Ako nije prvo pokretanje dodaje se i prethodno potroseno vreme (Program.TimeSpent)
            TimeSpan totalSpentTime = nowTime - Program.CurrentTime + (Program.SpentTime ?? TimeSpan.Zero);
            _writer.WriteLine(totalSpentTime.Milliseconds);

            _writer.WriteLine($"{Program.NumberOfGenerals},{_m},{(_commanderLoyal ? 1 : 0)},{_messageValue}");
            _writer.WriteLine($"{string.Join(',', _unloyalGenerals)}");
            _writer.WriteLine();
            _writer.Flush();
            Program.Logger.Verbose("Simulation information written");

            //Upis RSA parametara
            CertificationAuthority.Instance.WriteRSAParameters(_writer);
            
            //Upis prosledjenih poruka
            Message.CheckpointMessages(_writer);
        }
    }
}
