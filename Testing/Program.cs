using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Testing 
{

    public class RSASerializer
    {
        public static void SerializeRSAParameters(RSAParameters rsaParameters, string filePath, bool privateKey)
        {
            // Open a StreamWriter to write data to a file
            using StreamWriter writer = new(filePath);

            if (rsaParameters.Modulus is null || rsaParameters.Exponent is null)
            {
                throw new ArgumentException("Modulus or Exponent is null");
            }
            // Write the Modulus and Exponent (for public key)
            writer.WriteLine("Modulus: " + Convert.ToBase64String(rsaParameters.Modulus));
            writer.WriteLine("Exponent: " + Convert.ToBase64String(rsaParameters.Exponent));

            if (!privateKey)
            {
                return;
            }

            if (rsaParameters.D is null || rsaParameters.P is null || rsaParameters.Q is null
                || rsaParameters.DP is null || rsaParameters.DQ is null || rsaParameters.InverseQ is null)
            {
                throw new ArgumentException("Private key is missing commponents");
            }

            // If the private key is available, write all additional parameters 
            writer.WriteLine("D: " + Convert.ToBase64String(rsaParameters.D));
            writer.WriteLine("P: " + Convert.ToBase64String(rsaParameters.P));
            writer.WriteLine("Q: " + Convert.ToBase64String(rsaParameters.Q));
            writer.WriteLine("DP: " + Convert.ToBase64String(rsaParameters.DP));
            writer.WriteLine("DQ: " + Convert.ToBase64String(rsaParameters.DQ));
            writer.WriteLine("InverseQ: " + Convert.ToBase64String(rsaParameters.InverseQ));
        }

        public static RSAParameters DeserializeRSAParameters(string filePath)
        {
            RSAParameters rsaParameters = new();

            using (StreamReader reader = new(filePath))
            {
                // Read lines from the file and convert from Base64
                rsaParameters.Modulus = Convert.FromBase64String(reader.ReadLine()!.Split(':')[1].Trim());
                rsaParameters.Exponent = Convert.FromBase64String(reader.ReadLine()!.Split(':')[1].Trim());

                // If private key data exists, read it as well
                if (reader.Peek() >= 0)
                {
                    rsaParameters.D = Convert.FromBase64String(reader.ReadLine()!.Split(':')[1].Trim());
                    rsaParameters.P = Convert.FromBase64String(reader.ReadLine()!.Split(':')[1].Trim());
                    rsaParameters.Q = Convert.FromBase64String(reader.ReadLine()!.Split(':')[1].Trim());
                    rsaParameters.DP = Convert.FromBase64String(reader.ReadLine()!.Split(':')[1].Trim());
                    rsaParameters.DQ = Convert.FromBase64String(reader.ReadLine()!.Split(':')[1].Trim());
                    rsaParameters.InverseQ = Convert.FromBase64String(reader.ReadLine()!.Split(':')[1].Trim());
                }
            }

            return rsaParameters;
        }
    }

    public class Program
    {
        private static StreamWriter CreateWriter(string fileName)
        {
            int fileIndex = 1;
            string newFilePath = $"../../../logs/{fileName}";
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
        public static void Main()
        {
            string fileName = "test.txt";
            _ = CreateWriter(fileName);

            // 1. RSA parameters are initialized
            using RSA rsa = RSA.Create();
            RSAParameters rsaParams = rsa.ExportParameters(true); // Export private key too

            // 2. A string message is signed with those parameters
            string message = "This is a test message.";
            byte[] messageBytes = Encoding.UTF8.GetBytes(message);
            byte[] signedMessage;

            using (RSACryptoServiceProvider rsaProvider = new())
            {
                rsaProvider.ImportParameters(rsaParams);
                signedMessage = rsaProvider.SignData(messageBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            }

            // Output the signed message in Base64
            Console.WriteLine("Signed Message (Base64): " + Convert.ToBase64String(signedMessage));

            // 3. RSA parameters are serialized to a file
            string filePath = "rsaParameters.txt";
            RSASerializer.SerializeRSAParameters(rsaParams, filePath, true);
            Console.WriteLine("RSA parameters serialized to file.");

            // 4. New set of parameters is generated using deserialization
            RSAParameters deserializedParams = RSASerializer.DeserializeRSAParameters(filePath);
            Console.WriteLine("RSA parameters deserialized from file.");

            // 5. Message signature is checked
            using RSACryptoServiceProvider rsaVerify = new();
            rsaVerify.ImportParameters(deserializedParams);

            bool isValidSignature = rsaVerify.VerifyData(messageBytes, signedMessage, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            Console.WriteLine("Signature valid: " + isValidSignature);
        }
    }
}