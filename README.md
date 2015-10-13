# RDSDump
BACPAC generator for Amazon RDS SQL Server.

It is a C#.NET Console Application that search for the latest origin database Snapshot, restore it on a temporary RDS instance, generate a BACPAC file, upload it to S3 and delete the temporary RDS instance. All required parameters are defined on **Configurations** section.



###Dependencies
These application depends on three components which need to be installed on a Windows Server 2008 R2 SP1+ ou Windows 7 SP1+. 
- .NET Framework 4.5.1
- T-SQL ScriptDom (SqlDom.msi)
- SQL Server System CLR types (SQLSysCLRTypes.msi)

You can find those packages on:

- [Microsoft SQL Server 2014 Feature Pack] (https://www.microsoft.com/en-us/download/details.aspx?id=42295)
- [Microsoft .NET Framework 4.5.1] (https://www.microsoft.com/pt-br/download/details.aspx?id=40779) => Some Windows version already include this package.



###Configurations
First off all you have to configure the running machine with [AWS credentials] (http://docs.aws.amazon.com/powershell/latest/userguide/specifying-your-aws-credentials.html).

You can build the application on a Visual Studio Express 2013+. Before running the application you need to add some System Environment Variables.

- DatabaseNameOrigin => The DBInstanceIdentifier given to your RDS instance, that contains at least one automated Snapshot created.
- Database => The Origin Database name.
- DatabaseUser => The Origin Database User Name.
- DatabasePassword => The Origin Database Password for DatabaseUser.
- DatabaseNameTarget => The DBInstanceIdentifier for the temporary backup instance. (Any non-existent RDS DBInstanceIdentifier).
- S3BucketName => The name of BACPAC bucket on AWS S3.




