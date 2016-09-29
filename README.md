# service-bus-dotnet-messaging-performance
This sample can be used to help benchmark Service Bus Premium Messaging, and can be used for performance best practices. Check out the related blog post for additional information - <https://blogs.msdn.microsoft.com/servicebus/2016/07/18/premium-messaging-how-fast-is-it/>

## Running this sample

### Prerequisites

1. Visual Studio - <https://www.visualstudio.com/products/vs-2015-product-editions>
1. An Azure subscription
1. An Azure VM that is located in the same region as the Service Bus Namespace - <https://azure.microsoft.com/en-us/documentation/articles/virtual-machines-windows-hero-tutorial/>

### Create a Service Bus Namespace

1. Log on to the Azure classic portal
1. In the left navigation pane of the portal, click **Service Bus**.
1. In the lower pane of the portal, click **Create**.
1. In the **Add a new namespace** dialog, enter a namespace name. The system immediately checks to see if the name is available.
1. After making sure the namespace name is available, choose the country or region in which your namespace should be hosted.
1. Select **Premium** for the messaging tier, then Click the OK check mark. The system now creates your namespace and enables it. You might have to wait several minutes as the system provisions resources for your account.

### Obtain the credentials

1. In the left navigation pane, click the **Service Bus node**, to display the list of available namespaces:
1. Select the namespace you just created from the list shown.
1. Click **Connection Information**.
1. In the **Access connection information pane**, find the connection string that contains the SAS key and key name.
1. Make a note of the key, or copy it to the clipboard.

For more detailed instructions on creating a Service Bus Namespace, please follow step 1 of this tutorial: <https://azure.microsoft.com/en-us/documentation/articles/service-bus-dotnet-get-started-with-queues/#1-create-a-namespace-using-the-azure-portal>

## About the code
Since the primary goal of this sample was to show maximum throughput, we followed the best practices below:

1. Use AMQP as the message transport protocol
1. Use async where available
1. Use Partitioning (which is always enabled on premium)
1. Avoiding advanced features such as transactions, sessions, and duplicate detection
