using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ByzantineFailures
{
    /// <summary>
    /// Klasa koja predstavlja glavnog generala (izvor) u simulaciji
    /// </summary>
    /// <param name="isLoyal">Indikator da li je general lojalan</param>
    /// <param name="index">Indeks generala u sistemu</param>
    /// <param name="messageValue">Vrednost poruke koju glavni general salje svima</param>
    internal class CommandingGeneral(bool isLoyal, int index, int messageValue) : AbstractGeneral(isLoyal, index)
    {
        //Vrednost poruke koja se salje
        private readonly int _messageValue = messageValue;

        //Recnik svih poruka koje je general poslao
        private readonly Dictionary<int, (Message, int[])> _sentMessages = [];

        /// <summary>
        /// Metoda za komunikaciju, glavni general salje poruku svim ostalim generalima
        /// </summary>
        /// <param name="token">Token je null u ovom slucaju</param>
        public override void Communication(CancellationToken token)
        {
            //Ako glavni general nije lojalan, on treba da posalje neispravnu vrednost nekom broju generala
            int wrongValues = 0;
            if (!_isLoyal) 
            {
                //Odredjivanje koliko pogresnih vrednosti ce biti poslato
                wrongValues = new Random().Next(1, Program.NumberOfGenerals - 1);
            }

            //Slanje poruke ostalim generalima
            for (int i = 0; i < Program.NumberOfGenerals; i++)
            {
                if (i == Index) continue;

                //Potencijalan izbor pogresne vrednosti
                int messageValue = _messageValue;
                if (wrongValues > 0)
                {
                    messageValue = new Random().Next(2 * Program.DefaultMessageValue);
                    wrongValues--;
                }
                
                //Slanje poruke generalu
                if (_sentMessages.TryGetValue(messageValue, out (Message, int[]) value)) 
                {
                    //Ako je vrednost vec poslata, dohvata se objekat poruke i salje se dalje
                    SendMessage(value.Item1, i);
                    _ = value.Item2.Append(i);
                }
                else
                {
                    //Ako vrednost nije do sad poslata, kreira se nova poruka, salje i cuva u recniku
                    Message message = new(_parameters, messageValue);
                    SendMessage(message, i);
                    _sentMessages.Add(messageValue, (message, [i]));
                }

                //Logovanje slanja poruke
                Logger.Information($"Sent message S({messageValue}{(messageValue == _messageValue ? "" : "*")}) " +
                    $"to Liuetenant {i}");
                Program.Logger.Information($"Commander sent message " +
                    $"S({messageValue}{(messageValue == _messageValue ? "" : "*")}) to Liuetenant {i}");

                Program.RandomSleep();
            }

            //Logovanje kraja izvrsavanja
            Logger.Information("Done");
            Program.Logger.Information("Commander done");
            Logger.Dispose();
        }

        /// <summary>
        /// Glavni general ne prima poruke, pa ova metoda nema funkcionalnost
        /// </summary>
        /// <param name="message"></param>
        public override void InsertMessage(Message message) { }

        /// <summary>
        /// Prethodno poslata poruka se samo smesta u recnik poslatih poruka
        /// </summary>
        /// <param name="message">Objekat prethodno poslate poruke</param>
        /// <param name="messageValue">Vrednost prethodno poslate poruke</param>
        public override void AddSentMessage(Message message, int messageValue)
        {
            //U objektu poruke se nalazi i lista generala koji su primili poruku
            _sentMessages.TryAdd(messageValue, (message, message.Recipients.ToArray()));
        }

        /// <summary>
        /// Glavni general nema primljene poruke
        /// </summary>
        /// <param name="message"></param>
        /// <param name="value"></param>
        public override void AddReceivedMessage(Message message, int value) { }
    }
}
