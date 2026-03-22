using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using Server.Core;
using Server.Utilities;

namespace Server.Listeners;

public sealed class Listener : Entity
{
    [MaxLength(50)]
    public string Name { get; set; }

    public ushort BindPort { get; set; }
    
    [MaxLength(254)]
    public string CoffName { get; set; }
    
    public byte[] Coff { get; set; }
    public byte[] PublicKey { get; set; }
    public byte[] PrivateKey { get; set; }
    public ListenerStatus Status { get; set; }

    public static Listener Create(string name, ushort bindPort, string coffPath, byte[] coff)
    {
        using var rsa = RSA.Create(1024);

        return new Listener
        {
            Id = Helpers.GenerateId(),
            Name = name,
            BindPort = bindPort,
            CoffName = coffPath,
            Coff = coff,
            PublicKey = rsa.ExportSubjectPublicKeyInfo(),
            PrivateKey = rsa.ExportRSAPrivateKey(),
            Status = ListenerStatus.Stopped
        };
    }
}