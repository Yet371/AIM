/*
 * Copyright (C)2014 Araz Farhang Dareshuri
 * This file is a part of Aegis Implicit Ssl Mailer (AIM)
 * Aegis Implicit Ssl Mailer is free software: 
 * you can redistribute it and/or modify it under the terms of the GNU General Public License as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version.
 * This program is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  
 * See the GNU General Public License for more details.
 * You should have received a copy of the GNU General Public License along with this program.  
 * If not, see <http://www.gnu.org/licenses/>.
 * If you need any more details please contact <a.farhang.d@gmail.com>
 * Aegis Implicitt Ssl Mailer is an implicit ssl package to use mine/smime messages on implict ssl servers
 */
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Net.Mail;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.IO;
using System.Net.Mime;
using System.Security.Cryptography;
using System.Threading;

namespace AegisImplicitMail
{

    /// <summary>
    /// Send Implicit Ssl and none Ssl Messages
    /// </summary>
    public class SmtpSocketClient : IDisposable
    {
        private readonly object testConnectionLock = new object();
        private readonly object sendMailLock = new object();
        const string AuthExtension = "AUTH";
        const string AuthNtlm = "NTLM";

        const int bufLen = 998; // to comply with RFC 5322 2.1.1. Line Length Limits
        private const string Gap = " ";
        const string AuthGssapi = "gssapi";
        const string AuthWDigest = "wdigest";

        /// <summary>
        /// Sets the transaction time out by default it is 200,000  (200 seconds)
        /// </summary>
        public int Timeout
        {
            get { return _timeout; }
            set { _timeout = value; }
        }
        #region variables
        /// <summary>
        /// Delegate for mail sent notification.
        /// </summary>


        /// <summary>
        /// The delegate function which is called after mail has been sent.
        /// </summary>
        public event SendCompletedEventHandler SendCompleted;
        private SmtpSocketConnection _con;
        private int _port;
        private int _timeout = 200000;
        private AuthenticationType _authMode = AuthenticationType.UseDefaultCredentials;
        private string _user;
        private string _password;
        private MimeMailMessage _mailMessage;
        private X509CertificateCollection ClientCertificates { get; set; }
        private string _errormsg = "";
        private string _host;
        /// <summary>
        /// Name of server.
        /// </summary>
        public string Host
        {
            get { return _host; }
            set
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    throw new ArgumentException("Host shouldn't be empty or null. Invalid host name.");
                }
                _host = value;
            }
        }

        /// <summary>
		/// Port number of server server.
		/// </summary>
		public int Port
        {
            get { return _port; }
            set
            {
                if (value <= 0)
                {
                    throw new ArgumentException("Invalid port.");
                }
                _port = value;
            }
        }


        /// <summary>
        /// Authentication type , you can have no authentication, plain or base64
        /// Username and password will be encoded to base64 if the mode is set as base64
        /// </summary>
        public AuthenticationType AuthenticationMode
        {
            get { return _authMode; }
            set { _authMode = value; }
        }

        /// <summary>
        /// User ID for authentication.
        /// </summary>
        public string User
        {
            get { return _user; }
            set { _user = value; }
        }

        /// <summary>
        /// Password for authentication.
        /// </summary>
        public string Password
        {
            get { return _password; }
            set { _password = value; }
        }

        /// <summary>
        /// Email contents and attachments
        /// </summary>
        public MimeMailMessage MailMessage
        {
            get { return _mailMessage; }
            set { _mailMessage = value; }
        }

        /// <summary>
        /// Authentication protocol 
        /// </summary>
        /// <value>for more information: <see cref="AegisImplicitMail.SslMode" /></value>
        public SslMode SslType { get; set; }

        /// <summary>
        /// Delivery Status Notification (DSN)
        /// </summary>
        /// <value>True if DNS is supported</value>
        public bool DsnEnabled { get; private set; }

        /// <summary>
        /// Internationalized Email Addresses (Internet-Draft, 2007)
        /// </summary>
        /// <value>True if EAI is supported</value>
        public bool ServerSupportsEai { get; private set; }

        /// <summary>
        /// Supporting of TLS protocol
        /// </summary>
        /// <value>true if TLS is supported</value>
        public bool SupportsTls { get; private set; }


        #endregion

        #region constructor

        /// <summary>
        /// Generate a smtp socket client object
        /// </summary>
        /// <param name="host">Host address</param>
        /// <param name="port">Port Number</param>
        /// <param name="username">User name to login into server</param>
        /// <param name="password">Password</param>
        /// <param name="authenticationMode">Mode of authentication</param>
        /// <param name="useHtml">Determine if mail message is html or not</param>
        /// <param name="msg">Message to send</param>
        /// <param name="onMailSend">This function will be called after mail is sent</param>
        /// <param name="sslType">The type of Ssl used in your Smtp Mail Server <see cref="SslMode"/> </param>
        /// <exception cref="ArgumentNullException">If username and pass is needed and not provided</exception>
        public SmtpSocketClient(string host, int port = 465, string username = null, string password = null, AuthenticationType authenticationMode = AuthenticationType.Base64, MimeMailMessage msg = null, SendCompletedEventHandler onMailSend = null, SslMode sslType = SslMode.None)
            : this(msg)
        {
            if ((AuthenticationMode != AuthenticationType.UseDefaultCredentials) &&
                (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password)))
            {
                throw new ArgumentNullException("username");
            }

            _host = host;
            _port = port;
            _user = username;
            _password = password;
            _authMode = authenticationMode;
            _mailMessage = msg;
            SendCompleted = onMailSend;
            SslType = sslType;

        }

        /// <summary>
        /// Create a smtp socket client
        /// </summary>
        /// <param name="msg">Message to be sent</param>
        /// <param name="sslType">Authentication type</param>
        public SmtpSocketClient(MimeMailMessage msg = null, SslMode sslType = SslMode.None)
        {
            if (msg == null)
            {
                msg = new MimeMailMessage();
            }
            _mailMessage = msg;
            SslType = sslType;
        }

        #endregion



        /// <summary>
        /// Check and return the supported protocol
        /// </summary>
        /// <returns>Supported protocol <see cref="AegisImplicitMail.SslMode" /></returns>
        public SslMode DetectSslMode()
        {
            using (new MimeMailer(Host, Port, User, Password))
            {
                SslType = SslMode.Auto;
                if (TestConnection())
                {
                    if (SupportsTls)
                    {
                        return SslMode.Tls;
                    }
                }
                else
                {
                    SslType = SslMode.Ssl;
                    if (TestConnection())
                    {
                        return SslMode.Ssl;
                    }
                }
            }
            return SslMode.None;
        }


        /// <summary>
        /// Test if your settings are correct
        /// </summary>
        /// <returns>Return true if your connection settings are correct , otherwise false</returns>
        public bool TestConnection()
        {
            lock (testConnectionLock)
            {
                if (string.IsNullOrWhiteSpace(_host))
                {
                    throw new ArgumentException("There wasn't any host address found for the mail.");
                }
                if (_authMode != AuthenticationType.UseDefaultCredentials)
                {
                    if (string.IsNullOrWhiteSpace(_user))
                    {
                        throw new ArgumentException(
                            "You must specify user name when you are not using default credentials");
                    }

                    if (string.IsNullOrWhiteSpace(_password))
                    {
                        throw new ArgumentException(
                            "You must specify password when you are not using default credentials");
                    }
                }

                if (InCall)
                {
                    throw new InvalidOperationException("Mime mailer is busy already, please try later");
                }

                InCall = true;
                //set up initial connection
                var result = EstablishSmtp();
                string response;
                int code;
                if (_con != null)
                    QuiteConnection(out response, out code);
                return result;
            }
        }




        private bool EstablishSmtp()
        {

            _con = new SmtpSocketConnection();
            if (ClientCertificates != null)
            {
                _con.clientcerts = ClientCertificates;
            }
            if (_port <= 0)
            {
                _port = 465;
                SslType = SslMode.Ssl;
            }
            try
            {
                _con.Open(_host, _port, SslType, _timeout);
            }
            catch (Exception err)
            {
                if (SendCompleted != null)
                {
                    SendCompleted(this,
                        new AsyncCompletedEventArgs(
                            err, true, err.Message));
                }
                Dispose();
                return false;
            }
            string response;
            int code;
            //read greeting
            _con.GetReply(out response, out code);


            //Code 220 means that service is up and working

            if (!ParseGreeting(code, response)) return false;
            var buf = new StringBuilder();
            if (_authMode == AuthenticationType.UseDefaultCredentials)
            {
                buf.Append(SmtpCommands.Hello);
                buf.Append(_host);
                _con.SendCommand(buf.ToString());
                _con.GetReply(out response, out code);

                //Handle Errors
                if (!ParseHello(code, response)) return false;
            }
            else
            {
                buf.Append(SmtpCommands.EHello);
                buf.Append(_host);
                _con.SendCommand(buf.ToString());
                _con.GetReply(out response, out code);

                //Handle Errors
                if (!ParseEHello(code, response)) return false;

                var lines = response.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                ParseExtensions(lines);

                if (SslType == SslMode.Tls || SslType == SslMode.Auto)
                {
                    if (SupportsTls)
                    {
                        _con.SendCommand(SmtpCommands.StartTls);
                        _con.GetReply(out response, out code);
                        if (!ParseStartTls(code, response)) return false;
                        _con.SwitchToSsl();

                        //outlook365 needs EHLO to be resent
                        _con.SendCommand(buf.ToString());
                        _con.GetReply(out response, out code);
                    }
                    else
                    {

                        return false;
                    }
                }

                switch (_authMode)
                {
                    case AuthenticationType.Base64:

                        if (!AuthenticateAsBase64(out response, out code))
                        {
                            if (code == (int)SmtpResponseCodes.SyntaxError)
                            {
                                if (SendCompleted != null)
                                {
                                    SendCompleted(this,
                                        new AsyncCompletedEventArgs(
                                         new ServerException("Service Does not support Base64 Encoding. Please check authentification type"), true, response));
                                }
                            }
                            if (code == (int)SmtpResponseCodes.AuthenticationFailed)
                            {
                                if (SendCompleted != null)
                                {
                                    SendCompleted(this,
                                        new AsyncCompletedEventArgs(
                                            new ServerException("SMTP client authenticates but the username or password is incorrect"), true, response));
                                }
                            }
                            else if (code == (int)SmtpResponseCodes.Error)
                            {
                                if (SendCompleted != null)
                                {
                                    SendCompleted(this,
                                        new AsyncCompletedEventArgs(
                                            new ServerException("A general Error happened"), true, response));
                                }
                            }

                            else
                            {
                                if (SendCompleted != null)
                                {
                                    SendCompleted(this,
                                        new AsyncCompletedEventArgs(
                                            new ServerException("Authentication Failed"), true, response));
                                }
                            }
                            QuiteConnection(out response, out code);
                            return false;
                        }

                        break;

                    case AuthenticationType.PlainText:

                        if (!AuthenticateAsPlainText(out response, out code))
                        {
                            if (code == (int)SmtpResponseCodes.SyntaxError)
                            {
                                if (SendCompleted != null)
                                {
                                    SendCompleted(this,
                                        new AsyncCompletedEventArgs(
                                         new ServerException("Service Does not support plain text Encoding. Please check authentification type"), true, response));
                                }
                                return false;
                            }
                            if (code == (int)SmtpResponseCodes.AuthenticationFailed)
                            {
                                if (SendCompleted != null)
                                {
                                    SendCompleted(this,
                                        new AsyncCompletedEventArgs(
                                            new ServerException("SMTP client authenticates but the username or password is incorrect"), true, response));
                                }
                            }
                            else if (code == (int)SmtpResponseCodes.Error)
                            {
                                if (SendCompleted != null)
                                {
                                    SendCompleted(this,
                                        new AsyncCompletedEventArgs(
                                            new ServerException("General Error Happened"), true, response));
                                }
                            }

                            else
                            {
                                if (SendCompleted != null)
                                {
                                    SendCompleted(this,
                                        new AsyncCompletedEventArgs(
                                            new ServerException("Authentication Failed"), true, response));
                                }
                            }
                            QuiteConnection(out response, out code);
                            return false;
                        }
                        break;
                }
            }

            return true;
        }

        #region Parsers

        private bool ParseRcpt(int code, string response)
        {
            if (code == (int)SmtpResponseCodes.RequestCompleted) return true;
            //There is something wrong

            switch (code)
            {
                case (int)SmtpResponseCodes.ServiceNotAvailable:
                    _errormsg = "Service not available, closing transmission channel";
                    break;
                case (int)SmtpResponseCodes.MailNotAccepted:
                    _errormsg = "does not accept mail [rfc1846]";
                    break;
                case (int)SmtpResponseCodes.NotImplemented:
                    _errormsg = "Requested action not taken: mailbox unavailable";
                    break;
                case (int)SmtpResponseCodes.BadSequence:
                    _errormsg = "Bad sequence of commands";
                    break;
                case (int)SmtpResponseCodes.MailBoxUnavailable:
                    _errormsg = "Requested mail action not taken: mailbox unavailable";
                    break;
                case (int)SmtpResponseCodes.MailBoxNameNotValid:
                    _errormsg = "Requested action not taken: mailbox name not allowed";
                    break;
                case (int)SmtpResponseCodes.UserNotLocalBad:
                    _errormsg = "User not local";
                    break;
                case (int)SmtpResponseCodes.ExceededStorage:
                    _errormsg = "Requested mail action aborted: exceeded storage allocation";
                    break;
                case (int)SmtpResponseCodes.RequestAborted:
                    _errormsg = "Requested action aborted: local error in processing";
                    break;
                case (int)SmtpResponseCodes.InsufficientStorage:
                    _errormsg = "Requested action not taken: insufficient system storage";
                    break;
                case (int)SmtpResponseCodes.SyntaxError:
                    _errormsg = "Syntax error in parameters or arguments";
                    break;
                case (int)SmtpResponseCodes.Error:
                    _errormsg = "Syntax error, command unrecognised";
                    break;
            }

            if (SendCompleted != null)
            {
                SendCompleted(this,
                    new AsyncCompletedEventArgs(
                        new ServerException(_errormsg), true,
                        response));
            }
            QuiteConnection(out response, out code);
            return false;

        }


        private bool ParseMail(int code, string response)
        {
            if (code == (int)SmtpResponseCodes.RequestCompleted) return true;
            //There is something wrong

            switch (code)
            {
                case (int)SmtpResponseCodes.ServiceNotAvailable:
                    _errormsg = "Service not available, closing transmission channel";
                    break;
                case (int)SmtpResponseCodes.ExceededStorage:
                    _errormsg = "Requested mail action aborted: exceeded storage allocation";
                    break;
                case (int)SmtpResponseCodes.RequestAborted:
                    _errormsg = "Requested action aborted: local error in processing";
                    break;
                case (int)SmtpResponseCodes.InsufficientStorage:
                    _errormsg = "Requested action not taken: insufficient system storage";
                    break;
                case (int)SmtpResponseCodes.SyntaxError:
                    _errormsg = "Syntax error in parameters or arguments";
                    break;
                case (int)SmtpResponseCodes.Error:
                    _errormsg = "Syntax error, command unrecognised";
                    break;
            }

            if (SendCompleted != null)
            {
                SendCompleted(this,
                    new AsyncCompletedEventArgs(
                        new ServerException(_errormsg), true,
                        response));
            }
            QuiteConnection(out response, out code);
            return false;

        }

        private bool ParseStartTls(int code, string response)
        {
            if (code != (int)SmtpResponseCodes.Ready && code != (int)SmtpResponseCodes.RequestCompleted)
            {
                if (SendCompleted != null)
                {
                    SendCompleted(this, new AsyncCompletedEventArgs(new ServerException(response), true, response));
                }
                QuiteConnection(out response, out code);
                return false;
            }
            return true;
        }

        private bool ParseEHello(int code, string response)
        {
            if (code == (int)SmtpResponseCodes.RequestCompleted) return true;
            switch (code)
            {
                case (int)SmtpResponseCodes.ServiceNotAvailable:
                    _errormsg = "Service not available, closing transmission channel";
                    break;
                case (int)SmtpResponseCodes.NotImplemented:
                    _errormsg = "Not Implemented";
                    break;
                case (int)SmtpResponseCodes.CommandParameterNotImplemented:
                    _errormsg = "Command parameter not implemented";
                    break;
                case (int)SmtpResponseCodes.SyntaxError:
                    _errormsg = "Syntax error in parameters or arguments";
                    break;
                case (int)SmtpResponseCodes.Error:
                    _errormsg = "Syntax error, command unrecognised";
                    break;
                default:
                    _errormsg = response;
                    break;
            }
            if (SendCompleted != null)
            {
                SendCompleted(this, new AsyncCompletedEventArgs(new ServerException(_errormsg), true, response));
            }
            QuiteConnection(out response, out code);
            return false;
        }

        private bool ParseHello(int code, string response)
        {
            if (code == (int)SmtpResponseCodes.RequestCompleted) return true;
            switch (code)
            {
                case (int)SmtpResponseCodes.ServiceNotAvailable:
                    _errormsg = "Service not available, closing transmission channel";
                    break;
                case (int)SmtpResponseCodes.MailNotAccepted:
                    _errormsg = "does not accept mail [rfc1846]";
                    break;
                case (int)SmtpResponseCodes.CommandParameterNotImplemented:
                    _errormsg = "Command parameter not implemented";
                    break;
                case (int)SmtpResponseCodes.SyntaxError:
                    _errormsg = "Syntax error in parameters or arguments";
                    break;
                case (int)SmtpResponseCodes.Error:
                    _errormsg = "Syntax error, command unrecognised";
                    break;
                default:
                    _errormsg = response;
                    break;
            }
            if (SendCompleted != null)
            {
                SendCompleted(this, new AsyncCompletedEventArgs(new ServerException(_errormsg), true, response));
            }
            QuiteConnection(out response, out code);
            return false;
        }

        private bool ParseGreeting(int code, string response)
        {
            if (code == (int)SmtpResponseCodes.Ready) return true;
            //There is something wrong
            switch (code)
            {
                case (int)SmtpResponseCodes.ServiceNotAvailable:
                    _errormsg = "Service not available, closing transmission channel";
                    break;
                default:
                    _errormsg = "We couldn't connect to server, server is clossing";
                    break;
            }
            if (SendCompleted != null)
            {
                SendCompleted(this,
                    new AsyncCompletedEventArgs(
                        new ServerException(_errormsg), true,
                        response));
            }
            QuiteConnection(out response, out code);
            return false;
        }

        private bool ParseData(int code, string response)
        {
            if (code == (int)SmtpResponseCodes.StartInput || code == (int)SmtpResponseCodes.RequestCompleted) return true;
            switch (code)
            {
                case (int)SmtpResponseCodes.RequestAborted:
                    _errormsg = "Requested action aborted: local error in processing";
                    break;
                case (int)SmtpResponseCodes.TransactionFailed:
                    _errormsg = "Transaction failed";
                    break;
                case (int)SmtpResponseCodes.SyntaxError:
                    _errormsg = "Syntax error, command unrecognised";
                    break;
                case (int)SmtpResponseCodes.Error:
                    _errormsg = "Syntax error in parameters or arguments";
                    break;
                case (int)SmtpResponseCodes.BadSequence:
                    _errormsg = "Bad sequence of commands";
                    break;
                case (int)SmtpResponseCodes.ServiceNotAvailable:
                    _errormsg = "Service not available, closing transmission channel received data";
                    break;

                case (int)SmtpResponseCodes.ExceededStorage:
                    _errormsg = "Requested mail action aborted: exceeded storage allocation";
                    break;
                case (int)SmtpResponseCodes.InsufficientStorage:
                    _errormsg = "Requested action aborted: Insufficiant System Storage";
                    break;
                default:
                    _errormsg = "We couldn't connect to server, server is clossing";
                    break;
            }
            if (SendCompleted != null)
            {
                SendCompleted(this,
                    new AsyncCompletedEventArgs(
                        new ServerException(_errormsg), true,
                        response));
            }
            QuiteConnection(out response, out code);
            return false;
        }

        #endregion

        #region MessageSenders

        /// <summary>
        /// Send the message.
        /// </summary>
        public void SendMail(AbstractMailMessage message)
        {
            MailMessage = (MimeMailMessage)message;
            lock (sendMailLock)
            {
                if (string.IsNullOrWhiteSpace(_host))
                {
                    throw new ArgumentException("There wasn't any host address found for the mail.");
                }
                if (_authMode != AuthenticationType.UseDefaultCredentials)
                {
                    if (string.IsNullOrWhiteSpace(_user))
                    {
                        throw new ArgumentException(
                            "You must specify user name when you are not using defualt credentials");
                    }

                    if (string.IsNullOrWhiteSpace(_password))
                    {
                        throw new ArgumentException(
                            "You must specify password when you are not using defualt credentials");
                    }
                }

                if (InCall)
                {
                    throw new InvalidOperationException("Mime mailer is busy already, please try later");
                }

                if (String.IsNullOrEmpty(MailMessage.From.Address))
                {
                    throw new Exception("There wasn't any sender for the message");
                }
                if (MailMessage.To.Count == 0)
                {
                    throw new Exception("Please specifie at least one reciever for the message");
                }

                InCall = true;
                //set up initial connection
                if (EstablishSmtp())
                {

                    string response;
                    int code;
                    var buf = new StringBuilder { Length = 0 };
                    buf.Append(SmtpCommands.Mail);
                    buf.Append("<");
                    buf.Append(MailMessage.From.Address);
                    buf.Append(">");

                    _con.SendCommand(buf.ToString());
                    _con.GetReply(out response, out code);
                    if (!ParseMail(code, response)) return;

                    Console.Out.WriteLine("From Response :" + response);

                    buf.Length = 0;
                    //set up list of to addresses
                    foreach (MailAddress recipient in MailMessage.To)
                    {
                        buf.Append(SmtpCommands.Recipient);
                        buf.Append("<");
                        buf.Append(recipient.Address);
                        buf.Append(">");
                        _con.SendCommand(buf.ToString());
                        _con.GetReply(out response, out code);
                        if (!ParseRcpt(code, response)) return;
                        buf.Length = 0;
                    }
                    //set up list of cc addresses
                    buf.Length = 0;
                    foreach (MailAddress recipient in MailMessage.CC)
                    {
                        buf.Append(SmtpCommands.Recipient);
                        buf.Append("<");
                        buf.Append(recipient.Address);
                        buf.Append(">");
                        _con.SendCommand(buf.ToString());
                        _con.GetReply(out response, out code);
                        if (!ParseRcpt(code, response)) return;

                        buf.Length = 0;
                    }
                    //set up list of bcc addresses
                    buf.Length = 0;
                    foreach (MailAddress o in MailMessage.Bcc)
                    {
                        buf.Append(SmtpCommands.Recipient);
                        buf.Append("<");
                        buf.Append(o.Address);
                        buf.Append(">");
                        _con.SendCommand(buf.ToString());
                        _con.GetReply(out response, out code);
                        if (!ParseRcpt(code, response)) return;

                        buf.Length = 0;
                    }
                    buf.Length = 0;

                    //set headers
                    _con.SendCommand(SmtpCommands.Data);
                    _con.GetReply(out response, out code);
                    if (!ParseData(code, response)) return;
                    _con.SendCommand("X-Mailer: AIM.MimeMailer");
                    DateTime today = DateTime.UtcNow;
                    buf.Append(SmtpCommands.Date);
                    buf.Append(today.ToString("r"));
                    _con.SendCommand(buf.ToString());
                    buf.Length = 0;
                    buf.Append(SmtpCommands.From);
                    buf.Append(MailMessage.From);
                    _con.SendCommand(buf.ToString());
                    buf.Length = 0;
                    buf.Append(SmtpCommands.To);
                    buf.Append(MailMessage.To[0]);
                    for (int x = 1; x < MailMessage.To.Count; ++x)
                    {
                        buf.Append(";");
                        buf.Append(MailMessage.To[x]);
                    }
                    _con.SendCommand(buf.ToString());
                    if (MailMessage.CC.Count > 0)
                    {
                        buf.Length = 0;
                        buf.Append(SmtpCommands.Cc);
                        buf.Append(MailMessage.CC[0]);
                        for (int x = 1; x < MailMessage.CC.Count; ++x)
                        {
                            buf.Append(";");
                            buf.Append(MailMessage.CC[x]);
                        }
                        _con.SendCommand(buf.ToString());
                    }
                    if (MailMessage.Bcc.Count > 0)
                    {
                        buf.Length = 0;
                        buf.Append(SmtpCommands.Bcc);
                        buf.Append(MailMessage.Bcc[0]);
                        for (int x = 1; x < MailMessage.Bcc.Count; ++x)
                        {
                            buf.Append(";");
                            buf.Append(MailMessage.Bcc[x]);
                        }
                        _con.SendCommand(buf.ToString());
                    }

                    if (MailMessage.ReplyToList?.Count > 0)
                    {
                        foreach (MailAddress replyToAdr in MailMessage.ReplyToList)
                        {
                            buf.Length = 0;
                            buf.Append(SmtpCommands.ReplyTo);
                            buf.Append(replyToAdr);
                            _con.SendCommand(buf.ToString());
                        }
                    }

                    buf.Length = 0;
                    buf.Append(SmtpCommands.Subject);
                    String encodedSubject = GetEncodedSubject();
                    buf.Append(encodedSubject);
                    _con.SendCommand(buf.ToString());
                    SendMessageBody(buf);
                    _con.GetReply(out response, out code);
                    Console.Out.WriteLine("Sent Response :" + response);


                    _con.SendCommand(SmtpCommands.Quit);
                    _con.GetReply(out response, out code);

#if DEBUG
                    Console.WriteLine(response);
#endif

                    _con.Close();
                    InCall = false;

                    if (SendCompleted != null)
                    {
                        SendCompleted(this, new AsyncCompletedEventArgs(null, false, response));
                    }
                }
            }
        }

        private void SendMessageBody(StringBuilder buf)
        {
            var encodingString = TransferEncoder.GetContentTypeName(MailMessage.BodyEncoding);
            //Console.WriteLine(encodingString);
            //var encodingString = "iso-8859-1";
            //var encodingString = "utf-8";
            var encodingHtmlHeader = "Content-Type: text/html; charset=" + encodingString;
            var encodingPlainHeader = "Content-Type: text/plain; charset=" + encodingString;
            var encodingQuotedPrintable = MailMessage.BodyEncoding.Equals(Encoding.ASCII) ? "Content-Transfer-Encoding: quoted-printable\r\n" :
                "Content-Transfer-Encoding: base64\r\n";

            buf.Length = 0;
            //declare mime info for message
            _con.SendCommand("MIME-Version: 1.0");
            if (!MailMessage.IsBodyHtml ||
                (MailMessage.IsBodyHtml && ((MimeAttachment.InlineCount > 0) || (MimeAttachment.AttachCount > 0))))
            {
                _con.SendCommand("Content-Type: multipart/mixed; boundary=\"#SEPERATOR1#\"\r\n");
                _con.SendCommand("This is a multi-part message.\r\n\r\n--#SEPERATOR1#");
            }
            if (MailMessage.IsBodyHtml)
            {
                _con.SendCommand("Content-Type: multipart/related; boundary=\"#SEPERATOR2#\"");
                _con.SendCommand(encodingQuotedPrintable);
                _con.SendCommand("--#SEPERATOR2#");
            }
            if (MailMessage.IsBodyHtml && MimeAttachment.InlineCount > 0)
            {
                _con.SendCommand("Content-Type: multipart/alternative; boundary=\"#SEPERATOR3#\"");
                _con.SendCommand(encodingQuotedPrintable);
                _con.SendCommand("--#SEPERATOR3#");

                //      _con.SendCommand("Content-Type: text/html; charset=iso-8859-1");
                _con.SendCommand(encodingHtmlHeader);
                _con.SendCommand(encodingQuotedPrintable);
                _con.SendCommand(GetEncodedBody());
                _con.SendCommand("--#SEPERATOR3#");
                //   _con.SendCommand("Content-Type: text/plain; charset=iso-8859-1");
                _con.SendCommand(encodingPlainHeader);

                _con.SendCommand(
                    "\r\nIf you can see this, then your email client does not support MHTML messages.");
                _con.SendCommand("--#SEPERATOR3#--\r\n");
                _con.SendCommand("--#SEPERATOR2#\r\n");
                SendAttachments(buf, AttachmentLocation.Inline);
            }
            else
            {
                if (MailMessage.IsBodyHtml)
                {
                    _con.SendCommand(encodingHtmlHeader);
                    _con.SendCommand(encodingQuotedPrintable);
                    //     _con.SendCommand(" BODY=8BITMIME SMTPUTF8\r\n");


                }
                else
                {
                    _con.SendCommand(encodingPlainHeader);
                    _con.SendCommand(encodingQuotedPrintable);
                    //   _con.SendCommand(" BODY=8BITMIME SMTPUTF8\r\n");
                }
                //_con.SendCommand(MailMessage.Body);
                _con.SendCommand(GetEncodedBody());
            }
            if (MailMessage.IsBodyHtml)
            {
                _con.SendCommand("\r\n--#SEPERATOR2#--");
            }
            if (MimeAttachment.AttachCount > 0)
            {
                //send normal attachments
                SendAttachments(buf, AttachmentLocation.Attachmed);
            }
            //finish up message
            _con.SendCommand("");
            if (!MailMessage.IsBodyHtml || MimeAttachment.InlineCount > 0 || MimeAttachment.AttachCount > 0)
            {
                _con.SendCommand("--#SEPERATOR1#--");
            }

            _con.SendCommand(".");
        }


        public bool InCall { get; private set; }

        private bool AuthenticateAsPlainText(out string response, out int code)
        {
            _con.SendCommand(SmtpCommands.Auth + SmtpCommands.AuthLogin + Gap + SmtpCommands.AuthPlian);
            _con.GetReply(out response, out code);
            if (code == (int)SmtpResponseCodes.SyntaxError)
                return false;

            _con.SendCommand(_user);
            _con.GetReply(out response, out code);
            Console.Out.WriteLine("Reply to Plain 2: " + response + " Code :" + code);

            _con.SendCommand(_password);
            _con.GetReply(out response, out code);
            Console.Out.WriteLine("Reply to Plain 3: " + response + " Code :" + code);
            if (code == (int)SmtpResponseCodes.AuthenticationSuccessfull)
                return true;
            return false;

        }

        private bool AuthenticateAsBase64(out string response, out int code)
        {
            _con.SendCommand(SmtpCommands.Auth + SmtpCommands.AuthLogin);
            _con.GetReply(out response, out code);
            if (code == (int)SmtpResponseCodes.SyntaxError)
            {
                return false;
            }

            _con.SendCommand(Convert.ToBase64String(Encoding.ASCII.GetBytes(_user)));
            _con.GetReply(out response, out code);
            Console.Out.WriteLine("Reply to b64 2: " + response + " Code :" + code);

            _con.SendCommand(Convert.ToBase64String(Encoding.ASCII.GetBytes(_password)));

            _con.GetReply(out response, out code);
            Console.Out.WriteLine("Reply to b64 3: " + response + " Code :" + code);

            if (code == (int)SmtpResponseCodes.AuthenticationSuccessfull)
                return true;
            return false;
        }



        private void QuiteConnection(out string response, out int code)
        {
            try
            {
                _con.SendCommand(SmtpCommands.Quit);
                _con.GetReply(out response, out code);
                _con.Close();
            }
            finally
            {
                InCall = false;
                _con = null;
            }
        }

        /// <summary>
		/// Synchronous message sending on a separate thread .
		/// </summary>
		public void SendMailAsync(AbstractMailMessage message = null)
        {
            if (message == null)
                message = MailMessage;
            new Thread(() => SendMail(message)).Start();
        }

        /// <summary>
        /// Send any attachments.
        /// </summary>
        /// <param name="buf">String work area.</param>
        /// <param name="type">Attachment type to send.</param>
        private void SendAttachments(StringBuilder buf, AttachmentLocation type)
        {

            //declare mime info for attachment
            var fbuf = new byte[bufLen];
            string seperator = type == AttachmentLocation.Attachmed ? "\r\n--#SEPERATOR1#" : "\r\n--#SEPERATOR2#";
            buf.Length = 0;
            foreach (MimeAttachment o in MailMessage.Attachments)
            {
                MimeAttachment attachment = o;
                if (attachment.Location != type)
                {
                    continue;
                }

                Stream stream;
                string fileName;
                if (string.IsNullOrEmpty(attachment.FileName))
                {
                    stream = attachment.ContentStream;
                    fileName = attachment.Name;
                }
                else
                {
                    stream = new FileStream(attachment.FileName, FileMode.Open, FileAccess.Read, FileShare.Read);
                    fileName = Path.GetFileName(attachment.FileName);
                }

                var cs = new CryptoStream(stream, new ToBase64Transform(), CryptoStreamMode.Read);
                _con.SendCommand(seperator);
                var escapedFileName = fileName.Replace(@"\", @"\\").Replace(@"""", @"\""");
                buf.Append("Content-Type: ");
                buf.Append(attachment.ContentType);
                buf.Append("; name=\"");
                buf.Append(escapedFileName);
                buf.Append("\"");
                _con.SendCommand(buf.ToString());
                _con.SendCommand("Content-Transfer-Encoding: base64");
                buf.Length = 0;
                buf.Append("Content-Disposition: attachment; filename=\"");
                buf.Append(escapedFileName);
                buf.Append("\"");
                _con.SendCommand(buf.ToString());
                buf.Length = 0;
                buf.Append("Content-ID: ");
                var escapedContentId = "<" + (!string.IsNullOrEmpty(attachment.ContentId) ? attachment.ContentId : Path.GetFileNameWithoutExtension(fileName).Replace(" ", "-")) + ">";
                buf.Append(escapedContentId);
                buf.Append("\r\n");
                _con.SendCommand(buf.ToString());
                buf.Length = 0;
                int num = cs.Read(fbuf, 0, bufLen);
                char[] bufln = new char[2] { '\r', '\n' };
                while (num > 0)
                {
                    _con.SendData(Encoding.ASCII.GetChars(fbuf, 0, num), 0, num);
                    _con.SendData(bufln, 0, 2);
                    num = cs.Read(fbuf, 0, bufLen);
                }
                cs.Close();
                _con.SendCommand("");
            }
        }

        #endregion
        private string GetEncodedSubject()
        {
            var subjectEncoding = MailMessage.SubjectEncoding ?? Encoding.UTF8;
            if (Encoding.ASCII.Equals(subjectEncoding))
            {
                return MailMessage.Subject;
            }
            else
            {
                var encodingName = subjectEncoding.BodyName.ToLower();
                return "=?" + encodingName + "?B?" + TransferEncoder.ToBase64WithoutLinebrakes(subjectEncoding.GetBytes(MailMessage.Subject)) + "?=";
            }
        }

        private string GetEncodedBody()
        {
            if (MailMessage.BodyEncoding.Equals(Encoding.ASCII))
            {
                return BodyToQuotedPrintable();
            }
            else
            {
                return TransferEncoder.ToBase64(MailMessage.BodyEncoding.GetBytes(MailMessage.Body));
            }
        }
        /// <summary>
		/// Encode the body as in quoted-printable format.
		/// Adapted from PJ Naughter's quoted-printable encoding code.
		/// For more information see RFC 2045.
		/// </summary>
		/// <returns>The encoded body.</returns>
		private string BodyToQuotedPrintable()
        {
            //         var ENCODED = Encoding.UTF8.GetString(MailMessage.Body);
            var stringBuilder = new StringBuilder();
            sbyte currentByte;
            foreach (char t in MailMessage.Body)
            {
                currentByte = (sbyte)t;
                //is this a valid ascii character?
                if (((currentByte >= 33) && (currentByte <= 60)) || ((currentByte >= 62) && (currentByte <= 126)) || (currentByte == '\r') || (currentByte == '\n') || (currentByte == '\t') || (currentByte == ' '))
                {
                    stringBuilder.Append(t);
                }
                else
                {
                    stringBuilder.Append('=');
                    stringBuilder.Append(((sbyte)((currentByte & 0xF0) >> 4)).ToString("X"));
                    stringBuilder.Append(((sbyte)(currentByte & 0x0F)).ToString("X"));
                }
            }
            //format data so that lines don't end with spaces (if so, add a trailing '='), etc.
            //for more detail see RFC 2045.
            int start = 0;
            string encodedString = stringBuilder.ToString();
            stringBuilder.Length = 0;
            for (int x = 0; x < encodedString.Length; ++x)
            {
                currentByte = (sbyte)encodedString[x];
                if (currentByte == '\n' || currentByte == '\r' || x == (encodedString.Length - 1))
                {
                    stringBuilder.Append(encodedString.Substring(start, x - start + 1));
                    start = x + 1;
                    continue;
                }
                if ((x - start) > 76)
                {
                    bool inWord = true;
                    while (inWord)
                    {
                        inWord = (!char.IsWhiteSpace(encodedString, x) && encodedString[x - 2] != '=');
                        if (inWord)
                        {
                            --x;
                            //							currentByte = (sbyte) encodedString[x];
                        }
                        if (x == start)
                        {
                            x = start + 76;
                            break;
                        }
                    }
                    stringBuilder.Append(encodedString.Substring(start, x - start + 1));
                    stringBuilder.Append("=\r\n");
                    start = x + 1;
                }
            }
            return stringBuilder.ToString();
        }

        public void Dispose()
        {
            if (_con != null && _con.Connected)
            {
                _con.Close();
            }
            if (_mailMessage != null)
                _mailMessage.Dispose();
        }

        [Flags]
        internal enum SupportedAuth
        {
            None = 0,
            Login = 1,
            NTLM = 2,
            GSSAPI = 4,
            WDigest = 8,
        }

        internal void ParseExtensions(string[] extensions)
        {
            int sizeOfAuthExtension = AuthExtension.Length;

            var supportedAuth = SupportedAuth.None;
            foreach (string extension in extensions)
            {
                var realextension = extension;
                if (realextension.Length > 3)
                    realextension = extension.Substring(4);
                Console.Out.WriteLine("Extenstion :" + extension);
                if (String.Compare(realextension, 0, AuthExtension, 0,
                    sizeOfAuthExtension, StringComparison.OrdinalIgnoreCase) == 0)
                {
                    // remove the AUTH text including the following character 
                    // to ensure that split only gets the modules supported
                    var authTypes =
                        realextension.Remove(0, sizeOfAuthExtension).Split(new[] { ' ', '=' },
                        StringSplitOptions.RemoveEmptyEntries);
                    foreach (string authType in authTypes)
                    {
                        if (String.Compare(authType, SmtpCommands.AuthLogin, StringComparison.OrdinalIgnoreCase) == 0)
                        {
                            supportedAuth |= SupportedAuth.Login;
                        }
#if !FEATURE_PAL
                        else if (String.Compare(authType, AuthNtlm, StringComparison.OrdinalIgnoreCase) == 0)
                        {
                            supportedAuth |= SupportedAuth.NTLM;
                        }
                        else if (String.Compare(authType, AuthGssapi, StringComparison.OrdinalIgnoreCase) == 0)
                        {
                            supportedAuth |= SupportedAuth.GSSAPI;
                        }
                        else if (String.Compare(authType, AuthWDigest, StringComparison.OrdinalIgnoreCase) == 0)
                        {
                            supportedAuth |= SupportedAuth.WDigest;
                        }
#endif // FEATURE_PAL
                    }
                }
                else if (String.Compare(realextension, 0, "dsn ", 0, 3, StringComparison.OrdinalIgnoreCase) == 0)
                {
                    DsnEnabled = true;
                }
                else if (String.Compare(realextension, 0, SmtpCommands.StartTls, 0, 8, StringComparison.OrdinalIgnoreCase) == 0)
                {
                    SupportsTls = true;
                }
                else if (String.Compare(realextension, 0, SmtpCommands.Utf8, 0, 8, StringComparison.OrdinalIgnoreCase) == 0)
                {
                    ServerSupportsEai = true;
                }
            }
        }

    }
}
