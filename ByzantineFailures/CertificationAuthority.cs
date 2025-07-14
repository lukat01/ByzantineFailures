using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace ByzantineFailures
{
    /// <summary>
    /// Singleton klasa za generisanje i distribuciju kljuceva
    /// </summary>
    internal class CertificationAuthority
    {
        //Instanca klase
        private static readonly CertificationAuthority _instance = new();

        //Liste RSA parametara
        private List<RSAParameters> _privateKeys = [];
        private List<RSAParameters> _publicKeys = [];

        /// <summary>
        /// Staticki getter za instancu klase
        /// </summary>
        public static CertificationAuthority Instance
        { 
            get 
            { 
                return _instance;     
            } 
        }

        /// <summary>
        /// Metoda za generisanje RSA kljuceva za sve generale
        /// </summary>
        /// <param name="numberOfPairs">Parametar koji govori koliko je generala u sistemu</param>
        public void GenerateKeyPairs(int numberOfPairs)
        { 
            for (int i = 0; i < numberOfPairs; i++) 
            {
                using RSACryptoServiceProvider rsa = new(2048);
                try
                {
                    //javni RSA parametri koji se cuvaju
                    RSAParameters publicKey = rsa.ExportParameters(false);
                    _publicKeys.Add(publicKey);

                    //privatni RSA parametri
                    RSAParameters privateKey = rsa.ExportParameters(true);
                    //privatni kontekst se cuva zbog checkpoint-a
                    _privateKeys.Add(privateKey);
                    //privatni kontekst se salje odgovarajucem generalu
                    Program.Generals[i].Parameters = privateKey;
                }
                finally
                {
                    rsa.PersistKeyInCsp = false;
                }
            } 
        }

        /// <summary>
        /// Metoda za restauraciju RSA parametara na osnovu checkpoint fajla
        /// </summary>
        /// <param name="checkpointReader">Objekat za citanje checkpoint fajla</param>
        public void LoadKeysFromCheckpoint(CheckpointReader checkpointReader)
        {
            //Citanje parametara iz fajla
            (_privateKeys, _publicKeys) = checkpointReader.LoadRSAParameters();

            //Postavljanje kljuceva kod generala
            for (int i = 0; i <  _privateKeys.Count; i++)
            {
                Program.Generals[i].Parameters = _privateKeys[i];
            }
        }

        /// <summary>
        /// Metoda za dohvatanje javnog kljuca odgovarajuceg generala, na osnovu indeksa
        /// </summary>
        /// <param name="index">Indeks generala za kog se dohvata javni kljuc</param>
        /// <returns></returns>
        /// <exception cref="ArgumentOutOfRangeException">Izuzetak u slucaju kada vrednost indeksa nije u ispravnom opsegu</exception>
        public RSAParameters GetPublicKey(int index)
        { 
            //Provera indeksa
            if (index < 0 || index >= _publicKeys.Count) 
            { 
                throw new ArgumentOutOfRangeException(nameof(index), "Invalid index."); 
            } 

            //Vracanje vrednosti
            return _publicKeys[index]; 
        }

        /// <summary>
        /// Metoda za cuvanje svih RSA parametara u checkpoint-u
        /// </summary>
        /// <param name="writer">StreamWriter objekat za checkpoint fajl</param>
        public void WriteRSAParameters(StreamWriter writer)
        {
            Program.Logger.Verbose("Writing RSA parameters");

            //Serijalizacija svih kljuceva
            for (int i = 0; i < _privateKeys.Count; i++) 
            { 
                SerializeRSAParameters(writer, _privateKeys[i], true);
                SerializeRSAParameters(writer, _publicKeys[i], false);
            }

            writer.WriteLine();
            writer.Flush();
            Program.Logger.Verbose("RSA parameters stored");
        }

        /// <summary>
        /// Metoda za serijalizaciju RSA parametara prilikom generisanja checkpoint-a
        /// </summary>
        /// <param name="writer">StreamWriter za checkpoint fajl</param>
        /// <param name="rsaParameters">RSA parametar</param>
        /// <param name="privateKey">Indikator koji govori da li se cuva privatni kljuc</param>
        /// <exception cref="ArgumentException">Izuzetak koji se javlja ako neki parametar nedostaje</exception>
        public static void SerializeRSAParameters(StreamWriter writer, RSAParameters rsaParameters, bool privateKey)
        {
            //Svaki kljuc mora da sadrzi Modulus i Exponent
            if (rsaParameters.Modulus is null || rsaParameters.Exponent is null)
            {
                throw new ArgumentException("Modulus or Exponent is null");
            }

            //Parametri javnog kljuca
            writer.WriteLine("Modulus: " + Convert.ToBase64String(rsaParameters.Modulus));
            writer.WriteLine("Exponent: " + Convert.ToBase64String(rsaParameters.Exponent));

            //Ako se cuva javni kljuc to je sve
            if (!privateKey)
            {
                return;
            }

            //Privatni kljuc mora da sadrzi D, P, Q, DP, DQ i InverseQ
            if (rsaParameters.D is null || rsaParameters.P is null || rsaParameters.Q is null
                || rsaParameters.DP is null || rsaParameters.DQ is null || rsaParameters.InverseQ is null)
            {
                throw new ArgumentException("Private key is missing commponents");
            }

            //Parametri privatnog kljuca
            writer.WriteLine("D: " + Convert.ToBase64String(rsaParameters.D));
            writer.WriteLine("P: " + Convert.ToBase64String(rsaParameters.P));
            writer.WriteLine("Q: " + Convert.ToBase64String(rsaParameters.Q));
            writer.WriteLine("DP: " + Convert.ToBase64String(rsaParameters.DP));
            writer.WriteLine("DQ: " + Convert.ToBase64String(rsaParameters.DQ));
            writer.WriteLine("InverseQ: " + Convert.ToBase64String(rsaParameters.InverseQ));
        }
    }
}
