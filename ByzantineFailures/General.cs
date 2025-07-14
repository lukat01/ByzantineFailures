using Serilog;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;

namespace ByzantineFailures
{
    /// <summary>
    /// Klasa koja prezentuje obicnog generala u sistemu
    /// </summary>
    /// <param name="isLoyal">Indikator da li je general lojalan</param>
    /// <param name="index">Indeks generala u sistemu</param>
    internal class General(bool isLoyal, int index) : AbstractGeneral(isLoyal, index)
    {
        //Konkurentan red za prijem prouka
        private readonly ConcurrentQueue<Message> _messageQueue = [];

        //Recnik u kome se cuvaju primljene vrednosti (kao kljucevi) i broj koliko puta je jedna vrednost primljena 
        private readonly Dictionary<int, int> _receivedValues = [];

        //Lista svih primljenih poruka
        private readonly List<Message> _receivedMessages = [];

        //Lista svih poslatih poruka
        private readonly List<Message> _sentMessages = [];

        /// <summary>
        /// Metoda za simulaciju komunikacije
        /// </summary>
        /// <param name="token">Token kojim se zahteva kraj izvrsavanja</param>
        public override void Communication(CancellationToken token)
        {
            Program.RandomSleep();
            //Dokle god se ne zahteva kraj izvrsavanja
            while (!token.IsCancellationRequested)
            {
                //Dohvatanje poruke iz reda
                Message? message = ReceiveMessage();

                //Ako je poruka null, pokusava se ponovno dohvatanje poruke
                if (message is null)
                {
                    continue;
                }

                //Dodavanje poruke u listu primljenih
                message.AddRecipient(Index);
                _receivedMessages.Add(message);

                //Analiza poruke i dohvatanje indikatora da li je ispravna, vrednost, sadrzaj poruke i listu potpisnika
                //Ako poruka nije validna, pozvana metoda vraca podrazumevanu vrednost
                (bool valid, int value, string messageData, int[] signers) = Message.CheckAndProcessMessage(message);

                //Dodavanje poruke u recnik
                //Provera da li je vec u recniku
                if (!_receivedValues.TryAdd(value, 1))
                {
                    //Ako jeste metoda u if uslovu vraca false, pa je potrebno samo inkrementirati vrednost
                    _receivedValues[value]++;
                }

                //Formatiranje ispisa primljene poruke
                //Prvi deo je sekvenca potpisnika, zatim se u zagradama nalazi vrednost
                //* oznacava da poruka nije validna, tj. da je izmenjena
                string signersString = string.Join(":", signers
                    .Select(s => s == Program.CommanderIndex ? "S" : $"R{s}"));

                //Logovanje informacija
                Logger.Information($"Received message: {signersString}({value}{(valid ? "" : "*")})");
                Program.Logger.Information($"Liueteneant {Index} received message: " +
                    $"{signersString}({value}{(valid ? "" : "*")})");

                //Skup koji sadrzi indekse prethodnih potpisnika
                HashSet<int> previousSenders = [];
                previousSenders.UnionWith(signers);

                Program.RandomSleep();

                //Generisanje nove poruke za slanje
                //Prethodna poruka se potpisuje i dodaje se indeks na kraj sekvence potpisnika
                Message message1 = new(_parameters, messageData, signers, Index);

                //Ako general nije lojalan, menja proizvoljan bajt u poruci
                if (!_isLoyal)
                {
                    message1.ChangeData();
                }

                //Dodavanje poruke u listu poslatih
                _sentMessages.Add(message1);

                //Slanje poruke ostalim generalima
                for (int i = 0; i < Program.NumberOfGenerals; i++)
                {
                    //Poruka se ne salje samom sebi, kao ni prethodnim potpisnicima
                    if (i != Index && i != Program.CommanderIndex && !previousSenders.Contains(i))
                    {
                        //Slanje poruke
                        SendMessage(message1, i);

                        //Logovanje slanja poruke
                        string signersString1 = string.Join(":", signers
                            .Append(Index)
                            .Select(s => s == Program.CommanderIndex ? "S" : $"R{s}"));
                        Logger.Information($"Sent message {signersString1}({value}{(_isLoyal && valid ? "" : "*")}) to liuetenant R{i}");
                        Program.Logger.Information($"Liueteneant {Index} sent message " +
                            $"{signersString1}({value}{(_isLoyal && valid ? "" : "*")}) to liuetenant R{i}");

                        Program.RandomSleep();
                    }
                }
            }

            //U slucaju da se radi checkpoint, komunikacija nije zavrsena, pa ne treba donositi odluku
            //Vec samo prekinuti komunikaciju
            if (!Program.CommunicationCompleted) 
            {
                Logger.Dispose();
                return;
            }

            //Nakon sto je komunikacija zavrsena, potrebno je odluciti koja vrednost se bira
            //Ako se u recniku nalazi jedna vrednost, ona se bira, u suprotnom, bira se podrazumevana vrednost
            int decision = _receivedValues.Count == 1
                ? _receivedValues.Keys.ToList().First()
                : Program.DefaultMessageValue;

            //int decision = _receivedValues.Count == 0
            //    ? _defaultMessageValue
            //    : _receivedValues.Aggregate((x, y) => x.Value > y.Value ? x : y).Key;

            //Logovanje svih primljenih vrednosti
            string allReceivedValues = "All received values: " 
                + string.Join(", ", _receivedValues.ToList().Select(v => $"{v.Key}({v.Value})"));
            Logger.Verbose(allReceivedValues);
            Program.Logger.Verbose($"Liuetenant {Index}: {allReceivedValues}");

            //Logovanje izabrane vrednosti
            Logger.Information($"Done, choosing {decision}");
            Program.Logger.Information($"Liuetenant {Index} done{(!_isLoyal ? "*" :"")}, chooses {decision}");
            Logger.Dispose();   
        }
        
        /// <summary>
        /// Dohvatanje poruke iz reda
        /// </summary>
        /// <returns>Povratna vrednost je poruka iz reda</returns>
        private Message? ReceiveMessage()
        {
            _ = _messageQueue.TryDequeue(out Message? message);
            return message;
        }


        /// <summary>
        /// Dodavanje poruke u konkurenti red
        /// </summary>
        /// <param name="message">Poruka koja se dodaje</param>
        public override void InsertMessage(Message message)
        {
            _messageQueue.Enqueue(message);
        }

        /// <summary>
        /// Metoda za dodavanje prethodno poslate poruke u listu
        /// Koristi se prilikom restauracije konteksta iz checkpointa
        /// </summary>
        /// <param name="message">Objekat prethodno poslate poruke</param>
        /// <param name="value">Vrednost prethodno poslate poruke, ne koristi se</param>
        public override void AddSentMessage(Message message, int value)
        {
            _sentMessages.Add(message);
        }

        /// <summary>
        /// Metoda za dodavanje prethodno primljene poruke u listu
        /// Koristi se prilikom restauracije konteksta iz checkpointa
        /// </summary>
        /// <param name="message">Objekat prethodno primljene poruke</param>
        /// <param name="value">Vrednost prethodno primljene poruke</param>
        public override void AddReceivedMessage(Message message, int value)
        {
            //Objekat se dodaje u listu
            _receivedMessages.Add(message);

            //Vrednost se dodaje u recnik primljenih vrednosti
            if (!_receivedValues.TryAdd(value, 1))
            {
                _receivedValues[value]++;
            }
        }
    }
}
