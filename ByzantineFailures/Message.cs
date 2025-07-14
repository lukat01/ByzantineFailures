using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Metadata;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ByzantineFailures
{
    /// <summary>
    /// Klasa koja predstavlja poruku u sistemu
    /// </summary>
    internal class Message
    {
        //Delimiter koji odvaja sekvencu potpisnika od sadrzaja poruke
        private const string SequenceDelimiter = "@#@";

        //Delimiter pojedinacnih potpisa u poruci
        private const string SignaturesDelimiter = "#@#";

        //Delimiter u okviru liste potpisnika
        private const string SignersDelimiter = ":";

        //Skup indeksa generala koji su primili poruku
        public HashSet<int> Recipients { get; private set; } = [];

        //Staticki _id koji se redom dodeljuje prilikom kreiranja nove poruke i inkrementira
        private static int _id = 0;

        //Lock za dodeljivanje i inkrementiranje identifikatora
        private static readonly Lock Lock = new();

        //Identifikator poruke
        public int Id { get; init; }

        //Lista sa svim porukama
        public static List<Message> AllMessages { get; } = [];

        /// <summary>
        /// Property u kome se cuva kompletan sadrzaj poruke
        /// Ukjucujuci vrendost, sve potpise i sekvencu slanja
        /// </summary>
        public string Data { get; private set; } = string.Empty;

        /// <summary>
        /// Konstruktor koji poziva obican general
        /// </summary>
        /// <param name="parameters">RSA kljucevi</param>
        /// <param name="message">Sadrzaj prethodne poruke</param>
        /// <param name="signers">Lista prethodnih potpisnika</param>
        /// <param name="sender">Indeks generala koji salje poruku</param>
        public Message(RSAParameters parameters, string message, int[] signers, int sender)
        {
            //Od stringa je neophodno dobiti niz bajtova
            byte[] fullMessageBytes = Encoding.UTF8.GetBytes(message);

            //Na osnovu niza bajtova se generise potpis poruke
            byte[] signatureBytes = SignMessage(fullMessageBytes, parameters);

            //Dodavanje trenutnog generala na kraj liste potpisnika
            int[] signerSequence = [.. signers, sender];

            //Spajanje sadrzaja, potpisa i izmenjene sekvence u jedan string
            Data = CombineMessage(message, signatureBytes, signerSequence);

            //Dodeljivanje Id
            lock (Lock)
            {
                Id = ++_id;
            }

            //Dodavanje poruke u listu svih poruka
            AllMessages.Add(this);
        }

        /// <summary>
        /// Metoda za postavljanje trenutne vrednosti statickog _id
        /// Koristi se prilikom restauracije konteksta
        /// </summary>
        /// <param name="value">Trenutna vrednost poslednjeg identifikatora</param>
        public static void SetStaticId(int value) => _id = value;

        /// <summary>
        /// Konstruktor koji poziva izvor
        /// </summary>
        /// <param name="parameters">RSA kljucevi</param>
        /// <param name="value">Vrednost poruke</param>
        public Message(RSAParameters parameters, int value)
        {
            //Vrednost poruke se konvertuje u niz bajtova
            byte[] messageBytes = Encoding.UTF8.GetBytes(value.ToString());

            //Na osnovu bajtova se generise potpis
            byte[] signatureBytes = SignMessage(messageBytes, parameters);

            //Izvor je jedini element u sekvenci potpisnika
            int[] signerSequence = [Program.CommanderIndex];

            //Spajanje sadrzaja, potpisa i sekvence u jedan string
            Data = CombineMessage(value.ToString(), signatureBytes, signerSequence);

            //Dodeljivanje Id
            lock (Lock)
            {
                Id = ++_id;
            }

            //Dodavanje poruke u listu svih poruka
            AllMessages.Add(this);
        }

        /// <summary>
        /// Konstruktor za poruku koja se restauira iz checkpoint-a
        /// </summary>
        /// <param name="id">Id poruke</param>
        /// <param name="data">Kompletan string sadrzaj poruke</param>
        /// <param name="recipients">Skup indeksa koji su primili poruku</param>
        public Message(int id, string data, HashSet<int> recipients)
        {
            Id = id;
            Data = data;
            Recipients = recipients;
            AllMessages.Add(this);
        }

        /// <summary>
        /// Metoda za dodavanje novog indeksa u skup generala koji su primili poruku
        /// </summary>
        /// <param name="index"></param>
        public void AddRecipient(int index)
        {
            Recipients.Add(index);
        }

        /// <summary>
        /// Metoda koja proverava ispravnost poruke
        /// </summary>
        /// <param name="message">Poruka koja se proverava</param>
        /// <returns>Vraca se cetvorka sa indikatorom da li je poruka validna, vrednoscu poruke, sadrzajem poruke i sekvencom slanja</returns>
        public static (bool, int, string, int[]) CheckAndProcessMessage(Message message)
        {
            //pozivom ExtractParts dobija se indikator da li je poruka validna,
            //par nizova sa porukama i potpisima iz prethodnih slanja poruke
            //sekvenca identifikatora prethodnih generala u skevenci
            (bool valid, string[] receivedMessages, byte[][] receivedSignaturesBytes, int[] receivedSignerSequence) = ExtractParts(message.Data);

            //Ako poruka nije validna, odmah se vraca rezultat
            //uzima se podrazumevana vrednost za vrednost poruke
            if (!valid)
            {
                return (false, Program.DefaultMessageValue, message.Data.Split(SequenceDelimiter)[0], receivedSignerSequence);
            }

            //provera integriteta poruke se proverava tako sto se potpisi proveravaju u obrnutom redosledu
            //u odnosu na dobijen redosled potpisnika
            bool validSignatures = true;
            for (int i = receivedSignerSequence.Length - 1; i >= 0; i--)
            {
                bool isSignatureValid = VerifySignature(receivedMessages[i], receivedSignaturesBytes[i],
                    CertificationAuthority.Instance.GetPublicKey(receivedSignerSequence[i]));
                if (!isSignatureValid)
                {
                    validSignatures = false;
                }
            }

            //Ako su svi potpisi uspesno validirani, vraca se vrednost poruke, ako ne, uzima se podrazumevana vrednost
            int finalValue = validSignatures ? int.Parse(receivedMessages[0]) : Program.DefaultMessageValue;

            //Na kraju se generise ceo sadrzaj poruke
            StringBuilder concatenatedBase64 = new(finalValue.ToString() + SignaturesDelimiter);
            for (int i = 0; i < receivedSignaturesBytes.Length; i++)
            {
                if (i != 0) concatenatedBase64.Append(SignaturesDelimiter);
                concatenatedBase64.Append(Convert.ToBase64String(receivedSignaturesBytes[i]));
            }

            return (validSignatures, finalValue, concatenatedBase64.ToString(), receivedSignerSequence);
        }


        /// <summary>
        /// Metoda za potpis poruke
        /// </summary>
        /// <param name="messageBytes">Bajtovi poruke koja se potpisuje</param>
        /// <param name="privateKey">Privatni kljuc generala koji potpisuje poruku</param>
        /// <returns>Povratna vrednost je niz bajtova u kojima se nalazi potpis poruke</returns>
        static byte[] SignMessage(byte[] messageBytes, RSAParameters privateKey)
        {
            using RSA rsa = RSA.Create();
            rsa.ImportParameters(privateKey);

            //Potpisivanje se radi koriscenjem SHA256 algoritma
            return rsa.SignData(messageBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }

        /// <summary>
        /// Metoda za proveru integriteta poruke
        /// </summary>
        /// <param name="message">Poruka koja je primljena</param>
        /// <param name="signatureBytes">Bajtovi koji predstavljaju potpis poruke</param>
        /// <param name="publicKey">Javni kljuc kojim se desifruje potpis</param>
        /// <returns>Indikator da li je poruka validna</returns>
        static bool VerifySignature(string message, byte[] signatureBytes, RSAParameters publicKey)
        {
            byte[] messageBytes = Encoding.UTF8.GetBytes(message);
            using RSA rsa = RSA.Create();
            rsa.ImportParameters(publicKey);
            return rsa.VerifyData(messageBytes, signatureBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }

        /// <summary>
        /// Metoda kojom se kompletna poruka pakuje u string
        /// </summary>
        /// <param name="message"></param>
        /// <param name="signatureBytes"></param>
        /// <param name="signerSequence"></param>
        /// <returns></returns>
        static string CombineMessage(string message, byte[] signatureBytes, int[] signerSequence)
        {
            //Konverzija niza bajtova potpisa u base64 string
            string signatureBase64 = Convert.ToBase64String(signatureBytes);

            //Spajanje vrednosti poruke se potpisom i sekvencom potpisnika
            string result = $"{message}{SignaturesDelimiter}{signatureBase64}{SequenceDelimiter}" +
                $"{string.Join(
                    SignersDelimiter, 
                    signerSequence
                        .Select(s => s == Program.CommanderIndex ? "S" : $"R{s}").ToArray())}";

            return result;
        }

        /// <summary>
        /// Metoda za dohvatanje delova poruke
        /// </summary>
        /// <param name="finalMessage">Kompletna poruka u string formatu</param>
        /// <returns>
        /// Uredjena cetvorka koja sadrzi indikator da li je poruka validna,
        /// Niz stringova koji predstavljaju vrednosti poruke prilikom svakog od slanja u sekvenci
        /// Niz potpisa za svako od slanja u sekvenci
        /// Niz identifikatora generala u sekvenci
        /// </returns>
        static (bool valid, string[] messages, byte[][] signatures, int[] signerSequence) ExtractParts(string finalMessage)
        {
            //Iz poruke se izdvajaju vrednost i svi potpisi
            string[] parts = finalMessage.Split([SignaturesDelimiter], StringSplitOptions.None);

            //Poslednji potpis u sebi sadrzi i redosled potpisa, pa se taj deo dodatno odvaja
            string[] lastSignatureAndSequence = parts[^1].Split(SequenceDelimiter);

            //Poslednji potpis je sada bez sekvence
            parts[^1] = lastSignatureAndSequence[0];

            //Vrednost poruke je prvi string u nizu
            string receivedMessage = parts[0];

            //Na osnovu sekvence potpisnika, kreira se niz sa njihovim indeksima
            int[] receivedSignerSequence = lastSignatureAndSequence[1]
                .Split(SignersDelimiter)
                .Select(s => s[0] == 'S' ? Program.CommanderIndex : int.Parse(s[1..]))
                .ToArray();

            //Ovaj uslov proverava da li je neki od delimitera u poruci promenjen
            //Posebna provera da na bi kasnije doslo do greski
            //Broj elemenata u parts je za 1 veci od broja potpisnika zato sto je prvi element vrednost poruke
            if (parts.Length != receivedSignerSequence.Length + 1)
            {
                return (false, [], [], receivedSignerSequence);
            }

            //Za kasniju proveru potpisa, potrebno je imati parove poruka:potpis za svako prethodno slanje poruke
            //Prva poruka je samo vrednost i njen potpis
            //Prilikom sledeceg slanja se cela primljena poruka (ukljucujuci i potpis) potpisuje i taj novi potpis se dodaje na kraj 
            List<string> receivedMessages = [receivedMessage];
            List<byte[]> receivedSignatures = [];

            //U niz potpisa se elementi parts (osim prvog) dodaju bez izmene
            //Dok se u receivedMessages dodaju prethodna poruka + novi potpis, kao sto je objasnjeno
            foreach (string signaturePart in parts[1..])
            {
                try
                {
                    receivedMessages.Add(receivedMessages.Last() + SignaturesDelimiter + signaturePart);
                    receivedSignatures.Add(Convert.FromBase64String(signaturePart));
                }
                catch (FormatException)
                {
                    //Ako je neki potpis promenjen moze doci do greske sa base64 formatom
                    //U tom slucaju se vraca prikladna vrednost
                    return (false, [], [], receivedSignerSequence);
                }
            }
            //Na kraju ce se u nizu poruka nalaziti poruka sa poslednjim potpisom, sto nije neophodno za proveru
            receivedMessages.RemoveAt(receivedMessages.Count - 1);

            //U ovom trenutku poruka je validna (format, potpisi se kasnije proveravaju)
            //pa se vracaju sve izdvojene vrednosti
            return (true, [.. receivedMessages], [.. receivedSignatures], receivedSignerSequence);
        }

        /// <summary>
        /// Metoda za cuvanje poruka u checkpoint fajlu
        /// </summary>
        /// <param name="writer">StreamWriter za checkpoint fajl</param>
        public static void CheckpointMessages(StreamWriter writer)
        {
            Program.Logger.Verbose("Writing previous messages");
            //Svaka poruka se ispisuje u sledecem formatu
            //Prvi red: Id
            //Drugi red: Data (ceo string sadrzaj)
            //Treci red: Lista primalaca
            foreach (Message message in AllMessages)
            {
                writer.WriteLine(message.Id);
                writer.WriteLine(message.Data);
                writer.WriteLine(string.Join(',', message.Recipients));
            }
            writer.Flush();
            Program.Logger.Verbose("Previous messages written");
        }

        /// <summary>
        /// Metoda u kojoj se proizvoljan bajt u telu poruke menja
        /// </summary>
        public void ChangeData()
        {
            //Poruka se konvertuje u niz bajtova
            byte[] message1Bytes = Encoding.UTF8.GetBytes(Data);

            //Bira se nasumican indeks u okviru sadrzaja poruke, tj do @#@
            Random random = new();
            int changeIndex = random.Next(0, Data.IndexOf(SequenceDelimiter));

            //Generisanje nove nasumicne vrednosti
            byte newChar = (byte)random.Next(256);

            //Promena bajta u poruci
            message1Bytes[changeIndex] = newChar;

            //Cuvanje nove vrednosti u Data property
            Data = Encoding.UTF8.GetString(message1Bytes);
        }
    }
}
