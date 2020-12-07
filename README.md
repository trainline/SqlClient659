# Repro for SQLClient concurrency issue

Repro for [SQLClient#659](https://github.com/dotnet/SqlClient/issues/659).

It is using `docker-compose` for ease of execution and using an `iptables` rule to simulate network failures (for that reason the container uses the `NET_ADMIN` capability).

Run as:

```shell
cd SqlClient659
docker-compose --env-file envs/2017-3.1.env build
docker-compose --env-file envs/2017-3.1.env up
```

There are other env files available in `envs` for different scenarios, file naming convention is SQLServer version, .NET Core version, MARS enabled.

When using a different env file is necessary to build again as the env variables are also used in the build process.

## Implementation

The implementation uses async code and transactions.
Due to the high amount of load and the network failures, the benchmark will record a certain number of network failures.
The actual issue is represented by the `Invalid` count in the printed stats: this indicate that an operation as returned the result of a different operation.

## Parameters

It could be necessary to tweak some parameters based on the hardware used, in particular it is reccommended to play with the connection string in `docker-compose.yml` and the number of tasks in `Program.cs`.

## Repro status

| env file | .NET version | SQL Server Version | MARS | Failures rate |
| -------- | ------------ | ------------------ | ---- | ------- |
| 2017-3.1-MARS | .NET Core 3.1 | 2017 Enterprise | Yes | High |
| 2017-3.1 | .NET Core 3.1 | 2017 Enterprise | No | Low |
| 2017-5.0-MARS | .NET 5.0 | 2017 Enterprise | Yes | High |
| 2017-5.0 | .NET 5.0 | 2017 Enterprise | No | Low |
| 2019-3.1-MARS | .NET Core 3.1 | 2019 Enterprise | Yes | High |
| 2019-3.1 | .NET Core 3.1 | 2019 Enterprise | No | Low |
| 2019-5.0-MARS | .NET 5.0 | 2019 Enterprise | Yes | High |
| 2019-5.0 | .NET 5.0 | 2019 Enterprise | No | Low |
