using System;
using System.Collections.Generic;
using System.Linq;
using ProWeighSuitePlatforms.Core.Modules;
using ProWeighSuitePlatforms.Modules.Contracts;

namespace ProWeighSuitePlatforms.Modules.Infrastructure
{
    public static class ProductModuleCatalog
    {
        private static IReadOnlyList<ProductModuleDefinition> _all =
            Array.Empty<ProductModuleDefinition>();
        private static IReadOnlyList<ProductModuleDefinition> _matrixDefinitions =
            Array.Empty<ProductModuleDefinition>();
        private static IReadOnlyList<ITransactionModule> _transactionModules =
            Array.Empty<ITransactionModule>();
        private static IReadOnlyList<string> _licenseGatedModuleIds =
            Array.Empty<string>();
        private static IReadOnlyDictionary<string, ProductModuleDefinition> _byId =
            new Dictionary<string, ProductModuleDefinition>(StringComparer.OrdinalIgnoreCase);

        public static IReadOnlyList<ProductModuleDefinition> All => _all;

        public static IReadOnlyList<ProductModuleDefinition> MatrixDefinitions => _matrixDefinitions;

        public static IReadOnlyList<ITransactionModule> TransactionModules => _transactionModules;

        public static IReadOnlyList<string> LicenseGatedModuleIds => _licenseGatedModuleIds;

        public static ProductModuleDefinition? Find(string moduleId)
        {
            if (string.IsNullOrWhiteSpace(moduleId))
                return null;

            return _byId.TryGetValue(moduleId, out var definition) ? definition : null;
        }

        public static void Initialize(IEnumerable<ProductModuleDefinition> definitions)
        {
            ArgumentNullException.ThrowIfNull(definitions);

            var all = definitions.ToList();
            var byId = new Dictionary<string, ProductModuleDefinition>(StringComparer.OrdinalIgnoreCase);
            foreach (var definition in all)
            {
                if (!string.IsNullOrWhiteSpace(definition.ModuleId))
                    byId[definition.ModuleId] = definition;
            }

            _all = all;
            _byId = byId;
            _matrixDefinitions = all.Where(definition => definition.ShowInModuleMatrix).ToList();
            _transactionModules = all
                .Where(definition => definition.IsLicenseGated)
                .Select(definition => (ITransactionModule)new TransactionModule(definition))
                .ToList();
            _licenseGatedModuleIds = all
                .Where(definition => definition.IsLicenseGated)
                .Select(definition => definition.ModuleId)
                .ToList();
        }

        private sealed record TransactionModule(ProductModuleDefinition Definition) : ITransactionModule
        {
            public string ModuleId => Definition.ModuleId;
            public string DisplayName => Definition.DisplayName;
            public string IconKey => Definition.IconKey;
            public string RequiredCapability => Definition.RequiredCapability;
            public string Description => Definition.Description;
        }
    }
}
