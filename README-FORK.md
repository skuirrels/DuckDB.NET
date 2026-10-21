# Skuirrels DuckDB.NET fork

This is an unofficial stable build from the
[`skuirrels/DuckDB.NET`](https://github.com/skuirrels/DuckDB.NET) fork. It
packages the consolidated performance work for DuckDB v2.0.0 under distinct
`Skuirrels.DuckDB.NET.*` package IDs.

Install the bundled provider explicitly:

```shell
dotnet add package Skuirrels.DuckDB.NET.Data.Full --version 2.0.0
```

NuGet packages:

- [`Skuirrels.DuckDB.NET.Data.Full`](https://www.nuget.org/packages/Skuirrels.DuckDB.NET.Data.Full/)
- [`Skuirrels.DuckDB.NET.Bindings.Full`](https://www.nuget.org/packages/Skuirrels.DuckDB.NET.Bindings.Full/)

The package keeps the official `DuckDB.NET.Data` namespaces and assembly names,
so application source code does not need to change. Do not reference this fork
package and the official `DuckDB.NET.Data.Full` package in the same dependency
graph because they contain assemblies with the same identities.

This release bundles DuckDB v2.0.0 and includes the consolidated appender,
parameter binding, prepared-command, result materialisation, and scoped-writer
optimisations from the fork. When equivalent upstream changes are released,
move back to the official `DuckDB.NET.Data.Full` package.

DuckDB 2.0 changes some behaviour that applications can notice:

- An open streaming result that is not read to its end blocks every other
  command and appender flush on its connection with a `ResourceInUse` error.
  Close streaming readers before reusing the connection.
- A database file created by DuckDB 2.0 cannot be opened by DuckDB 1.x. Add
  `storage_compatibility_version=v1.5.0` to the connection string to create
  files that 1.5 can read.
- `TIMESTAMP_TZ_NS`, `TIME_NS` and `GEOMETRY` columns can be read; `VARIANT`
  is not supported yet.

The original DuckDB.NET and DuckDB licences and attribution are included in the
package.

Report fork-specific problems in the
[`skuirrels/DuckDB.NET` issue tracker](https://github.com/skuirrels/DuckDB.NET/issues/new).
