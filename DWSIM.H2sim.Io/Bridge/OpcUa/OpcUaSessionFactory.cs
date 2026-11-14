using System;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Configuration;

namespace DWSIM.H2sim.Io
{
    /// <summary>Creates OPC UA client sessions and accepts the server certificate.</summary>
    public sealed class OpcUaSessionFactory
    {
        /// <summary>Application configuration used for the session.</summary>
        public ApplicationConfiguration Configuration { get; private set; }

        /// <summary>Creates a session for the endpoint in <paramref name="connection"/>.</summary>
        public Session CreateSession(ConnectionConfig connection, Action<Session, KeepAliveEventArgs> keepAlive)
        {
            var application = new ApplicationInstance
            {
                ApplicationName = "H2sim",
                ApplicationType = ApplicationType.Client
            };

            Configuration = application
                .Build("urn:h2sim:client", "urn:h2sim:client")
                .AsClient()
                .AddSecurityConfiguration("CN=H2sim", null, null, null)
                .SetAutoAcceptUntrustedCertificates(true)
                .SetAddAppCertToTrustedStore(true)
                .Create()
                .GetAwaiter().GetResult();
            Configuration.CertificateValidator.CertificateValidation += (sender, e) => e.Accept = true;
            application.CheckApplicationInstanceCertificate(true, 2048).GetAwaiter().GetResult();

            var useSecurity = !string.Equals(connection.SecurityPolicy, "None", StringComparison.OrdinalIgnoreCase);
            var description = CoreClientUtils.SelectEndpoint(Configuration, connection.EndpointUrl, useSecurity);
            var endpoint = new ConfiguredEndpoint(null, description, EndpointConfiguration.Create(Configuration));

            IUserIdentity identity = connection.Anonymous
                ? (IUserIdentity)new UserIdentity(new AnonymousIdentityToken())
                : new UserIdentity(connection.Username, connection.Password);

            var session = Session.Create(Configuration, endpoint, false, false, "H2sim", 60000, identity, null)
                .GetAwaiter().GetResult();
            session.KeepAlive += (sender, e) => keepAlive?.Invoke(session, e);

            return session;
        }

        /// <summary>Reads one numeric node with a temporary session (used by tests and diagnostics).</summary>
        public double ReadDouble(ConnectionConfig connection, string nodeId, string namespaceUri)
        {
            var session = CreateSession(connection, null);
            try
            {
                var id = OpcUaNodeResolver.Resolve(nodeId, namespaceUri, session.NamespaceUris);
                var value = session.ReadValue(id);
                return Convert.ToDouble(value.Value, System.Globalization.CultureInfo.InvariantCulture);
            }
            finally
            {
                try { session.Close(); } catch { }
            }
        }
    }
}