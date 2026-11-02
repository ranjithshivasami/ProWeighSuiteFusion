using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ProWeighSuitePlatforms.Core.Migrations;

public interface IModuleMigration
{
    string Id { get; }

    string ModuleId { get; }

    int Order { get; }

    Task ApplyAsync(CancellationToken cancellationToken = default);
}

public interface IMigrationRegistry
{
    IReadOnlyList<IModuleMigration> All { get; }

    IModuleMigration? Find(string id);
}

public interface IMigrationRegistryBuilder
{
    void Add(IModuleMigration migration);
}
