using Amazon;
using Amazon.RDS;
using Amazon.RDS.Model;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Util;
using Microsoft.SqlServer.Dac;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RDSDump
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Started Dump...");


            string databaseNameOrigin = System.Environment.GetEnvironmentVariable("DatabaseNameOrigin", EnvironmentVariableTarget.Machine);
            string databaseNameTarget = System.Environment.GetEnvironmentVariable("DatabaseNameTarget", EnvironmentVariableTarget.Machine);
            string database = System.Environment.GetEnvironmentVariable("Database", EnvironmentVariableTarget.Machine);
            string databaseUser = System.Environment.GetEnvironmentVariable("DatabaseUser", EnvironmentVariableTarget.Machine);
            string databasePassword = System.Environment.GetEnvironmentVariable("DatabasePassword", EnvironmentVariableTarget.Machine);
            string s3BucketName = System.Environment.GetEnvironmentVariable("S3BucketName", EnvironmentVariableTarget.Machine);

            try
            {
                using (AmazonRDSClient rdsClient = new AmazonRDSClient(RegionEndpoint.USWest2))
                {

                    DBSnapshot newerSnapshot = getNewerSnapshot(databaseNameOrigin, rdsClient);
                    Console.WriteLine("Snapshot found: " + newerSnapshot.DBSnapshotIdentifier);

                    restoreSnapshotIntoDBTarget(databaseNameTarget, rdsClient, newerSnapshot);
                    Console.WriteLine("Restoring Snapshot Into RDS account... ");
                    Thread.Sleep(60000);

                    string address = waitUntilDBTargetIsAvailable(databaseNameTarget, rdsClient);

                    Console.WriteLine("Snapshot Restored. Exporting BACPAC to file... ");
                    exportBacpac(database, databaseUser, databasePassword, address);

                    Console.WriteLine("BACPAC exported. Deleting RDS backup database... ");
                    deleteDatabaseByName(databaseNameTarget, rdsClient);


                }

                using (var s3Client = new AmazonS3Client(RegionEndpoint.SAEast1))
                {
                    Console.WriteLine("Uploading BACPAC to S3... ");
                    uploadFileToS3(s3BucketName, "dump.bacpac", s3Client);
                    Console.WriteLine("BACPAC Upload and temporaries deleted!");

                }

                Console.WriteLine("Dump completed successfully!");

            }
            catch (DacServicesException e)
            {
                Console.WriteLine("Error Encountered:{0} Inner Exception: {1}", e.Messages, e.InnerException);
            }
            catch (Exception e)
            {
                Console.WriteLine("Error Encountered:{0} Inner Exception: {1}", e.Message, e.InnerException);
            }
            Console.ReadKey();
        }



        private static void uploadFileToS3(string s3BucketName, string filePath, AmazonS3Client s3Client)
        {
            string bucketName = s3BucketName;
            if (!(AmazonS3Util.DoesS3BucketExist(s3Client, bucketName)))
            {
                CreateABucket(s3Client, bucketName);
            }

            PutObjectRequest request = new PutObjectRequest()
            {
                BucketName = bucketName,
                Key = DateTime.Now.ToShortDateString().Replace("/", "-") + ".bacpac",
                FilePath = filePath
            };
            PutObjectResponse response2 = s3Client.PutObject(request);
        }

        private static void deleteDatabaseByName(string databaseNameTarget, AmazonRDSClient rdsClient)
        {
            DeleteDBInstanceRequest deleteRequest = new DeleteDBInstanceRequest()
            {
                DBInstanceIdentifier = databaseNameTarget,
                SkipFinalSnapshot = true
            };
            DeleteDBInstanceResponse deleteResponse =
                rdsClient.DeleteDBInstance(deleteRequest);

            HttpStatusCode statusCode = deleteResponse.HttpStatusCode;

            if (!statusCode.Equals(HttpStatusCode.OK))
            {
                throw new Exception();
            }
        }

        private static void exportBacpac(string database, string databaseUser, string databasePassword, string address)
        {
            DacServices svc = new DacServices("Data Source=" + address + ";Initial Catalog=" + database + ";User ID=" + databaseUser + ";Password=" + databasePassword);

            svc.Message += new EventHandler<DacMessageEventArgs>(receiveDacServiceMessageEvent);
            svc.ProgressChanged += new EventHandler<DacProgressEventArgs>(receiveDacServiceProgessEvent);

            svc.ExportBacpac("dump.bacpac", database);
        }

        private static string waitUntilDBTargetIsAvailable(string databaseNameTarget, AmazonRDSClient rdsClient)
        {
            string status = "";
            string address = "";
            do
            {
                DescribeDBInstancesRequest describeInstancesRequest =
                    new DescribeDBInstancesRequest()
                    {
                        DBInstanceIdentifier = databaseNameTarget
                    };
                DescribeDBInstancesResponse describeInstancesResponse =
                    rdsClient.DescribeDBInstances(describeInstancesRequest);

                DBInstance dbInstance = describeInstancesResponse.DBInstances.First();

                status = dbInstance.DBInstanceStatus;

                if (dbInstance.Endpoint != null)
                {
                    address = dbInstance.Endpoint.Address;
                }
                Thread.Sleep(60000);
            }
            while (status != "available");
            return address;
        }

        private static void restoreSnapshotIntoDBTarget(string databaseNameTarget, AmazonRDSClient rdsClient, DBSnapshot newerSnapshot)
        {
            RestoreDBInstanceFromDBSnapshotRequest restoreRequest = new RestoreDBInstanceFromDBSnapshotRequest()
            {
                DBSnapshotIdentifier = newerSnapshot.DBSnapshotIdentifier,
                DBInstanceIdentifier = databaseNameTarget


            };

            rdsClient.RestoreDBInstanceFromDBSnapshot(restoreRequest);
        }

        private static DBSnapshot getNewerSnapshot(string databaseNameOrigin, AmazonRDSClient rdsClient)
        {
            DescribeDBSnapshotsRequest describeSnapshotsRequest =
            new DescribeDBSnapshotsRequest()
            {
                DBInstanceIdentifier = databaseNameOrigin,
                SnapshotType = "automated"


            };
            DescribeDBSnapshotsResponse describeSnapshotsResponse =
                rdsClient.DescribeDBSnapshots(describeSnapshotsRequest);


            List<DBSnapshot> snapshots = describeSnapshotsResponse.DBSnapshots;

            IEnumerable<DBSnapshot> orderedSnapshots = snapshots.OrderByDescending(x => x.SnapshotCreateTime);

            DBSnapshot newerSnapshot = orderedSnapshots.First();
            return newerSnapshot;
        }




        static void receiveDacServiceMessageEvent(object sender, DacMessageEventArgs e)
        {
            Console.WriteLine(string.Format("Message Type:{0} Prefix:{1} Number:{2} Message:{3}", e.Message.MessageType, e.Message.Prefix, e.Message.Number, e.Message.Message));
        }

        static void receiveDacServiceProgessEvent(object sender, DacProgressEventArgs e)
        {
            Console.WriteLine(string.Format("Progress Event:{0} Progrss Status:{1}", e.Message, e.Status));
        }


        static string FindBucketLocation(IAmazonS3 client, string bucketName)
        {
            string bucketLocation;
            GetBucketLocationRequest request = new GetBucketLocationRequest()
            {
                BucketName = bucketName
            };
            GetBucketLocationResponse response = client.GetBucketLocation(request);
            bucketLocation = response.Location.ToString();
            return bucketLocation;
        }

        static void CreateABucket(IAmazonS3 client, string bucketName)
        {

            PutBucketRequest putRequest1 = new PutBucketRequest
            {
                BucketName = bucketName,
                UseClientRegion = true
            };

            PutBucketResponse response1 = client.PutBucket(putRequest1);

        }
    }
}
