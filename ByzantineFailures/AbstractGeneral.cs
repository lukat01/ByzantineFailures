using Serilog;
using Serilog.Core;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace ByzantineFailures
{
    /// <summary>
    /// Apstraktna klasa koja predstavlja generala u sistemu
    /// </summary>
    internal abstract class AbstractGeneral
    {
        //Polje koje govori da li je general lojalan
        protected readonly bool _isLoyal;

        //RSA kljucevi
        protected RSAParameters _parameters;

        //Indikator da li su RSA kljucevi postavljeni
        private bool _isSet = false;


        public AbstractGeneral(bool isLoyal, int index)
        {
            _isLoyal = isLoyal;
            Index = index;

            //Inicijalizacija Logger-a za generala
            //Logovi se ispisiuju u fajl, Logger za klasu Program je zaduzen za ispis u konzoli
            //Ovo funkcionise i u slucaju kada se simulacija pokrece od checkpoint-a
            //Zato sto je u tom slucaju StartTime vec promenjeno na prethodno pocetno vreme
            //Zbog toga se logovi samo nastavljaju u istom fajlu
            string generalRepresentation = Index == Program.CommanderIndex ? "S" : $"R{Index}";
            Logger = new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .WriteTo.File($"{Program.LogsFolder}/{generalRepresentation}.txt", rollingInterval: RollingInterval.Infinite)
                .CreateLogger();
        }

        //Indeks generala
        public int Index { get; init; }

        //Objekat za generisanje logova
        public Logger Logger { get; }

        // Property za kljuceve RSA seme
        public RSAParameters Parameters
        { 
            get 
            { 
                return _parameters; 
            } 
            set 
            { 
                //Provera da li je vrednost vec postavljena
                if (!_isSet) 
                { 
                    //Ako nije, vrednost se postavlja
                    _parameters = value; 
                    _isSet = true; 
                } 
                else 
                { 
                    //Ako je vrednost vec postavljena, izuzetak
                    throw new InvalidOperationException("Property can only be set once.");
                } 
            } 
        }

        /// <summary>
        /// Apstraktna metoda za simulaciju komunikacije generala
        /// </summary>
        /// <param name="token">Token sluzi za prekidanje izvrsavanja</param>
        public abstract void Communication(CancellationToken token);

        /// <summary>
        /// Metoda za slanje poruke generalu
        /// </summary>
        /// <param name="message">Poruka koja se salje</param>
        /// <param name="index">Indeks generala kome se salje poruka</param>
        /// <exception cref="IndexOutOfRangeException">Izuzetak u slucaju kada vrednost indeksa nije u ispravnom opsegu</exception>
        protected static void SendMessage(Message message, int index) 
        {
            //Provera indeksa
            if (index < 0 || Program.Generals.Count <= index)
            {
                throw new IndexOutOfRangeException("Index out of bounds");
            }

            //Dodavanje poruke u red odgovarajucem generalu
            Program.Generals[index].InsertMessage(message);
        }

        /// <summary>
        /// Apstraktna metoda za dodavanje poruke u red
        /// </summary>
        /// <param name="message">Poruka koja se dodaje</param>
        public abstract void InsertMessage(Message message);

        /// <summary>
        /// Dodavanje poruke u listu poslatih
        /// Poziva se kada se restauira kontekst na osnovu checkpoint-a
        /// </summary>
        /// <param name="message"></param>
        /// <param name="value"></param>
        public abstract void AddSentMessage(Message message, int value);

        /// <summary>
        /// Dodavanje poruke u listu primljenih
        /// Poziva se kada se restauira kontekst na osnovu checkpoint-a
        /// </summary>
        /// <param name="message"></param>
        /// <param name="value"></param>
        public abstract void AddReceivedMessage(Message message, int value);
    }
}
