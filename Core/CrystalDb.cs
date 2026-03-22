using Microsoft.EntityFrameworkCore;
using Server.Beacons;
using Server.Listeners;
using Server.Tasks;

namespace Server.Core;

public sealed class CrystalDb(DbContextOptions<CrystalDb> options) : DbContext(options)
{
    public DbSet<Listener> Listeners => Set<Listener>();
    public DbSet<Beacon> Beacons => Set<Beacon>();
    public DbSet<BeaconTask> Tasks => Set<BeaconTask>();
}