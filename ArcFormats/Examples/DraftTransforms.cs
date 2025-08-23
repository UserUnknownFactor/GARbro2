using System;
using System.IO;
using System.Text;
using System.Diagnostics;
using GameRes.Formats;

public class TransformStreamExamples
{
    public static void Main()
    {
        // Example 1: Using XorTransform with InputCryptoStream
        Example1_XorTransform();
        
        // Example 2: Using NotTransform
        Example2_NotTransform();
        
        // Example 3: Chaining multiple transforms
        Example3_ChainedTransforms();
        
        // Example 4: Using custom transform with file operations
        Example4_FileEncryption();
        
        // Example 5: Using transforms for both encryption and decryption
        Example5_SymmetricOperations();
    }

    static void Example1_XorTransform()
    {
        Trace.WriteLine ("=== Example 1: XOR Transform ===");
        
        string originalText = "Hello, World!";
        byte[] data = Encoding.UTF8.GetBytes (originalText);
        
        // Encrypt using XOR with key 0x42
        using (var inputStream = new MemoryStream (data))
        using (var xorTransform = new XorTransform (0x42))
        using (var cryptoStream = new InputCryptoStream (inputStream, xorTransform))
        using (var outputStream = new MemoryStream())
        {
            cryptoStream.CopyTo (outputStream);
            byte[] encrypted = outputStream.ToArray();
            
            Trace.WriteLine ($"Original: {originalText}");
            Trace.WriteLine ($"Encrypted (hex): {BitConverter.ToString (encrypted)}");
            
            // Decrypt (XOR is symmetric)
            using (var decryptStream = new MemoryStream (encrypted))
            using (var decryptTransform = new XorTransform (0x42))
            using (var decryptCryptoStream = new InputCryptoStream (decryptStream, decryptTransform))
            using (var reader = new StreamReader (decryptCryptoStream))
            {
                string decrypted = reader.ReadToEnd();
                Trace.WriteLine ($"Decrypted: {decrypted}");
            }
        }
        Trace.WriteLine();
    }

    static void Example2_NotTransform ()
    {
        Trace.WriteLine ("=== Example 2: NOT Transform ===");
        
        byte[] data = { 0x00, 0xFF, 0xAA, 0x55 };
        
        using (var inputStream = new MemoryStream (data))
        using (var notTransform = new NotTransform())
        using (var cryptoStream = new InputCryptoStream (inputStream, notTransform))
        {
            byte[] result = new byte[data.Length];
            cryptoStream.Read (result, 0, result.Length);
            
            Trace.WriteLine ($"Original: {BitConverter.ToString (data)}");
            Trace.WriteLine ($"NOT result: {BitConverter.ToString (result)}");
            
            // Verify: NOT is its own inverse
            using (var verifyStream = new MemoryStream (result))
            using (var verifyTransform = new NotTransform())
            using (var verifyCryptoStream = new InputCryptoStream (verifyStream, verifyTransform))
            {
                byte[] original = new byte[result.Length];
                verifyCryptoStream.Read (original, 0, original.Length);
                Trace.WriteLine ($"Double NOT: {BitConverter.ToString (original)}");
            }
        }
        Trace.WriteLine();
    }

    static void Example3_ChainedTransforms()
    {
        Trace.WriteLine ("=== Example 3: Chained Transforms ===");
        
        string text = "Secret Message";
        byte[] data = Encoding.UTF8.GetBytes (text);
        
        // First apply XOR, then Add, then Rotate
        using (var stream1 = new MemoryStream (data))
        using (var xorTransform = new XorTransform (0x5A))
        using (var xorStream = new InputCryptoStream (stream1, xorTransform))
        using (var stream2 = new MemoryStream())
        {
            // Apply XOR
            xorStream.CopyTo (stream2);
            stream2.Position = 0;
            
            // Apply Add
            using (var addTransform = new AddTransform (25))
            using (var addStream = new InputCryptoStream (stream2, addTransform))
            using (var stream3 = new MemoryStream())
            {
                addStream.CopyTo (stream3);
                stream3.Position = 0;
                
                // Apply Rotate Left
                using (var rotateTransform = new RotateLeftTransform (3))
                using (var rotateStream = new InputCryptoStream (stream3, rotateTransform))
                using (var finalStream = new MemoryStream())
                {
                    rotateStream.CopyTo (finalStream);
                    byte[] encrypted = finalStream.ToArray();
                    
                    Trace.WriteLine ($"Original: {text}");
                    Trace.WriteLine ($"Triple encrypted: {BitConverter.ToString (encrypted)}");
                    
                    // Decrypt in reverse order: Rotate Right, Subtract, XOR
                    DecryptChained (encrypted);
                }
            }
        }
        Trace.WriteLine();
    }

    static void DecryptChained (byte[] encrypted)
    {
        // Decrypt: Rotate Right -> Subtract -> XOR
        using (var stream1 = new MemoryStream (encrypted))
        using (var rotateTransform = new RotateRightTransform (3))
        using (var rotateStream = new InputCryptoStream (stream1, rotateTransform))
        using (var stream2 = new MemoryStream())
        {
            rotateStream.CopyTo (stream2);
            stream2.Position = 0;
            
            using (var subtractTransform = new SubtractTransform (25))
            using (var subtractStream = new InputCryptoStream (stream2, subtractTransform))
            using (var stream3 = new MemoryStream())
            {
                subtractStream.CopyTo (stream3);
                stream3.Position = 0;
                
                using (var xorTransform = new XorTransform (0x5A))
                using (var xorStream = new InputCryptoStream (stream3, xorTransform))
                using (var reader = new StreamReader (xorStream))
                {
                    string decrypted = reader.ReadToEnd();
                    Trace.WriteLine ($"Decrypted: {decrypted}");
                }
            }
        }
    }

    static void Example4_FileEncryption()
    {
        Trace.WriteLine ("=== Example 4: File Encryption ===");
        
        string inputFile = "test.txt";
        string encryptedFile = "test.encrypted";
        string decryptedFile = "test.decrypted.txt";
        
        // Create test file
        File.WriteAllText (inputFile, "This is a test file with some content.\nLine 2\nLine 3");
        
        // Encrypt file using XOR transform
        using (var inputStream = File.OpenRead (inputFile))
        using (var xorTransform = new XorTransform (0x7F))
        using (var cryptoStream = new InputCryptoStream (inputStream, xorTransform))
        using (var outputStream = File.Create (encryptedFile))
        {
            cryptoStream.CopyTo (outputStream);
            Trace.WriteLine ($"Encrypted file created: {encryptedFile}");
        }
        
        // Decrypt file
        using (var inputStream = File.OpenRead (encryptedFile))
        using (var xorTransform = new XorTransform (0x7F))
        using (var cryptoStream = new InputCryptoStream (inputStream, xorTransform))
        using (var outputStream = File.Create (decryptedFile))
        {
            cryptoStream.CopyTo (outputStream);
            Trace.WriteLine ($"Decrypted file created: {decryptedFile}");
        }
        
        // Verify
        string original = File.ReadAllText (inputFile);
        string decrypted = File.ReadAllText (decryptedFile);
        Trace.WriteLine ($"Files match: {original == decrypted}");
        
        // Cleanup
        File.Delete (inputFile);
        File.Delete (encryptedFile);
        File.Delete (decryptedFile);
        Trace.WriteLine();
    }

    static void Example5_SymmetricOperations()
    {
        Trace.WriteLine ("=== Example 5: Symmetric Operations ===");
        
        byte[] testData = { 0x01, 0x23, 0x45, 0x67, 0x89, 0xAB, 0xCD, 0xEF };
        
        // Demonstrate Add/Subtract symmetry
        Trace.WriteLine ("Add/Subtract Transform:");
        using (var stream = new MemoryStream (testData))
        using (var addTransform = new AddTransform (100))
        using (var addStream = new InputCryptoStream (stream, addTransform))
        using (var tempStream = new MemoryStream())
        {
            addStream.CopyTo (tempStream);
            byte[] added = tempStream.ToArray();
            Trace.WriteLine ($"Original:  {BitConverter.ToString (testData)}");
            Trace.WriteLine ($"Added 100: {BitConverter.ToString (added)}");
            
            // Subtract to restore
            using (var subStream = new MemoryStream (added))
            using (var subTransform = new SubtractTransform (100))
            using (var subCryptoStream = new InputCryptoStream (subStream, subTransform))
            using (var resultStream = new MemoryStream())
            {
                subCryptoStream.CopyTo (resultStream);
                byte[] restored = resultStream.ToArray();
                Trace.WriteLine ($"Sub 100:   {BitConverter.ToString (restored)}");
            }
        }
        
        // Demonstrate Rotate Left/Right symmetry
        Trace.WriteLine ("\nRotate Transform:");
        using (var stream = new MemoryStream (testData))
        using (var rotLeftTransform = new RotateLeftTransform (3))
        using (var rotLeftStream = new InputCryptoStream (stream, rotLeftTransform))
        using (var tempStream = new MemoryStream())
        {
            rotLeftStream.CopyTo (tempStream);
            byte[] rotated = tempStream.ToArray();
            Trace.WriteLine ($"Original:     {BitConverter.ToString (testData)}");
            Trace.WriteLine ($"Rotated L3:   {BitConverter.ToString (rotated)}");
            
            // Rotate right to restore
            using (var rotRightStream = new MemoryStream (rotated))
            using (var rotRightTransform = new RotateRightTransform (3))
            using (var rotRightCryptoStream = new InputCryptoStream (rotRightStream, rotRightTransform))
            using (var resultStream = new MemoryStream())
            {
                rotRightCryptoStream.CopyTo (resultStream);
                byte[] restored = resultStream.ToArray();
                Trace.WriteLine ($"Rotated R3:   {BitConverter.ToString (restored)}");
            }
        }
    }
}

// Example of creating a custom transform
public class CustomTransform : ByteTransform
{
    private readonly byte[] m_lookupTable;
    
    public CustomTransform()
    {
        // Create a simple substitution table
        m_lookupTable = new byte[256];
        for (int i = 0; i < 256; i++)
        {
            // Simple scrambling: swap nibbles and XOR with position
            m_lookupTable[i] = (byte)(((i << 4) | (i >> 4)) ^ i);
        }
    }
    
    public override int TransformBlock (byte[] inputBuffer, int inputOffset, int inputCount,
                                     byte[] outputBuffer, int outputOffset)
    {
        for (int i = 0; i < inputCount; ++i)
        {
            outputBuffer[outputOffset++] = m_lookupTable[inputBuffer[inputOffset + i]];
        }
        return inputCount;
    }
}

// Example usage with custom transform
public class CustomTransformExample
{
    public static void RunExample()
    {
        Trace.WriteLine ("=== Custom Transform Example ===");
        
        string text = "Custom Transform Test";
        byte[] data = Encoding.UTF8.GetBytes (text);
        
        using (var inputStream = new MemoryStream (data))
        using (var customTransform = new CustomTransform())
        using (var cryptoStream = new InputCryptoStream (inputStream, customTransform))
        using (var outputStream = new MemoryStream())
        {
            cryptoStream.CopyTo (outputStream);
            byte[] transformed = outputStream.ToArray();
            
            Trace.WriteLine ($"Original: {text}");
            Trace.WriteLine ($"Original bytes: {BitConverter.ToString (data)}");
            Trace.WriteLine ($"Transformed:    {BitConverter.ToString (transformed)}");
        }
    }
}