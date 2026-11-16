using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Configuration;

namespace DWSIM.H2sim.Io
{
    /// <summary>Creates OPC UA client sessions and accepts the server certificate.</summary>
    public sealed class OpcUaSessionFactory
    {
        private static readonly object CertificateLock = new object();
        private static X509Certificate2 _applicationCertificate;

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

            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                Configuration = application
                    .Build("urn:h2sim:client", "urn:h2sim:client")
                    .AsClient()
                    .AddSecurityConfiguration("CN=H2sim, DC=localhost", null, null, null)
                    .SetAutoAcceptUntrustedCertificates(true)
                    .SetAddAppCertToTrustedStore(true)
                    .Create()
                    .GetAwaiter().GetResult();
            }
            else
            {
                var pkiRoot = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "H2sim",
                    "pki");
                Directory.CreateDirectory(pkiRoot);
                Configuration = application
                    .Build("urn:h2sim:client", "urn:h2sim:client")
                    .AsClient()
                    .AddSecurityConfiguration(
                        "CN=H2sim, DC=localhost",
                        pkiRoot,
                        Path.Combine(pkiRoot, "own"),
                        Path.Combine(pkiRoot, "rejected"))
                    .SetAutoAcceptUntrustedCertificates(true)
                    .SetAddAppCertToTrustedStore(true)
                    .Create()
                    .GetAwaiter().GetResult();
            }
            Configuration.CertificateValidator.CertificateValidation += (sender, e) => e.Accept = true;
            EnsureApplicationCertificate(Configuration);

            var useSecurity = !string.Equals(connection.SecurityPolicy, "None", StringComparison.OrdinalIgnoreCase);
            var description = CoreClientUtils.SelectEndpoint(Configuration, connection.EndpointUrl, useSecurity);
            var endpoint = new ConfiguredEndpoint(null, description, EndpointConfiguration.Create(Configuration));

            IUserIdentity identity = connection.Anonymous
                ? (IUserIdentity)new UserIdentity(new AnonymousIdentityToken())
                : new UserIdentity(connection.Username, connection.Password);

            var session = Session.Create(Configuration, endpoint, false, false, "H2sim", 60000, identity, null)
                .GetAwaiter().GetResult();
            session.FetchNamespaceTables();
            session.KeepAlive += (sender, e) => keepAlive?.Invoke(session, e);

            return session;
        }

        private static void EnsureApplicationCertificate(ApplicationConfiguration configuration)
        {
            lock (CertificateLock)
            {
                if (_applicationCertificate == null)
                {
                    var domainNames = new List<string>
                    {
                        Utils.GetHostName(),
                        "localhost"
                    };
                    _applicationCertificate = CertificateFactory.CreateCertificate(
                            "urn:h2sim:client",
                            "H2sim",
                            "CN=H2sim, DC=localhost",
                            domainNames)
                        .SetLifeTime(60)
                        .SetRSAKeySize(2048)
                        .CreateForRSA();
                }

                configuration.SecurityConfiguration.ApplicationCertificate.Certificate = _applicationCertificate;
                configuration.CertificateValidator
                    .Update(configuration.SecurityConfiguration)
                    .GetAwaiter().GetResult();
            }
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
