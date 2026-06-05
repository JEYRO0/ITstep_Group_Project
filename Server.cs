using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection.Metadata;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Data.SqlClient;

namespace TeamProject
{
    public class User
    {
        public int Id;
        public string NickName;
        public string Password;
    }

    public class Wallet
    {
        public int UserId;
        public string WalletName;
        public string Currency;
        public decimal Balance;
    }


    public enum MessageType
    {
        RegisterRequest,
        RegisterResponse,
        LoginRequest,
        LoginResponse,
        AddWalletRequest,
        AddWalletResponse,
        LoadDataRequest,
        LoadDataResponse,
        Error
    }

    public class MessagePacket
    {
        public MessageType Type { get; set; }
        public string Data { get; set; } = null!;
        public string SenderName { get; set; } = null!;
    }


    public class Server
    {
        //Підючення до бази даних
        private static string connectionString = "Server=YOUR_SERVER;Database=YOUR_DATABASE;Trusted_Connection=True;TrustServerCertificate=True;";

        // Сховище активних мережевих клієнтів (Ключ: Id користувача в базі, Значення: його TcpClient)
        private static readonly ConcurrentDictionary<int, TcpClient> _authenticatedClients = new();


        static async Task Main(string[] args)
        {
            int port = 8888;
            TcpListener listener = new TcpListener(IPAddress.Any, port);
            listener.Start();
            Console.WriteLine($"Сервер запущено на порту {port}");

            try
            {
                while (true)
                {
                    TcpClient client = await listener.AcceptTcpClientAsync();
                    _ = HandleClientAsync(client);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CRITICAL] Помилка роботи сервера: {ex.Message}");
            }
            finally
            {
                listener.Stop();
            }
        }

        private static async Task HandleClientAsync(TcpClient client)
        {
            User? authenticatedUser = null;

            using (client)
            await using (NetworkStream stream = client.GetStream())
            {
                try
                {
                    while (true)
                    {
                        MessagePacket packet = await ReadMessageAsync(stream);
                        if (packet == null)
                            break;

                        switch (packet.Type)
                        {
                            case MessageType.RegisterRequest:
                                await HandleRegisterAsync(stream, packet.Data);
                                break;
                            case MessageType.LoginRequest:
                                authenticatedUser = await HandleLoginAsync(stream, client, packet.Data);
                                break;
                            case MessageType.AddWalletRequest:
                                if (authenticatedUser == null)
                                {
                                    await SendPacketAsync(stream, new MessagePacket { Type = MessageType.Error, Data = "Ви не авторизовані!" });
                                    break;
                                }
                                await HandleAddWalletAsync(stream, authenticatedUser.Id, packet.Data);
                                break;
                            case MessageType.LoadDataRequest:
                                if (authenticatedUser == null)
                                {
                                    await SendPacketAsync(stream, new MessagePacket { Type = MessageType.Error, Data = "Ви не авторизовані!" });
                                    break;
                                }
                                await HandleLoadDataAsync(stream, authenticatedUser.Id);
                                break;
                            default:
                                Console.WriteLine($"[WARNING] Отримано невідомий тип повідомлення: {packet.Type}");
                                break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERROR] Помилка обробки клієнта: {ex.Message}");
                }
            }

        }

        private static async Task HandleRegisterAsync(NetworkStream stream, string data)
        {

            try
            {
                var user = JsonSerializer.Deserialize<User>(data);
                if (user == null) return;

                using (IDbConnection db = new SqlConnection(connectionString))
                {
                    //перевіряємо чи існує вже користувач із цим нікнеймом
                    string checkSql = "SELECT COUNT(1) FROM Users WHERE NickName = @NickName";
                    int exists = await db.ExecuteScalarAsync<int>(checkSql, new { NickName = user.NickName });

                    if (exists > 0)
                    {
                        await SendPacketAsync(stream, new MessagePacket { Type = MessageType.RegisterResponse, Data = "Помилка: Користувач з таким нікнеймом вже існує!" });
                        return;
                    }

                    // Зберігаємо в базу даних
                    string insertSql = "INSERT INTO Users (NickName, Password) VALUES (@NickName, @Password);";
                    await db.ExecuteAsync(insertSql, user);
                }

                Console.WriteLine($"[SERVER] Здійснено нову реєстрацію: {user.NickName}");
                await SendPacketAsync(stream, new MessagePacket { Type = MessageType.RegisterResponse, Data = "Успіх: Реєстрація пройшла вдало!" });

            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Помилка реєстрації користувача: {ex.Message}");
                await SendPacketAsync(stream, new MessagePacket { Type = MessageType.Error, Data = "Помилка реєстрації!" });

            }
        }


        private static async Task<User?> HandleLoginAsync(NetworkStream stream, TcpClient client, string data)
        {
            try
            {
                var loginData = JsonSerializer.Deserialize<User>(data);
                if(loginData == null) return null;

                using (IDbConnection db = new SqlConnection(connectionString))
                {
                    string sql = "SELECT Id, NickName, Password FROM Users WHERE NickName = @NickName AND Password = @Password";
                    var user = await db.QueryFirstOrDefaultAsync<User>(sql, loginData);
                    if (user == null)
                    {
                        await SendPacketAsync(stream, new MessagePacket { Type = MessageType.LoginResponse, Data = "Помилка: Невірний нікнейм або пароль!" });
                        return null;
                    }

                    _authenticatedClients.TryAdd(user.Id, client);

                    Console.WriteLine($"[SERVER] Користувач {user.NickName} успішно авторизувався.");
                    await SendPacketAsync(stream, new MessagePacket { Type = MessageType.LoginResponse, Data = "Успіх: Авторизація пройшла вдало!" });
                    return user;
                }

            }
            catch (Exception ex) {
                Console.WriteLine($"[ERROR] Помилка авторизації користувача: {ex.Message}");
                await SendPacketAsync(stream, new MessagePacket { Type = MessageType.Error, Data = "Помилка авторизації!" });
                return null;
            }
        }


        private static async Task HandleAddWalletAsync(NetworkStream stream, int userId, string data)
        {
            try
            {
                var wallet = JsonSerializer.Deserialize<Wallet>(data);
                if (wallet == null) return;

                wallet.UserId = userId;
                using (IDbConnection db = new SqlConnection(connectionString))
                {
                    string sql = @"INSERT INTO Wallets (UserId, WalletName, Currency, Balance) 
                                   VALUES (@UserId, @WalletName, @Currency, @Balance);";
                    await db.ExecuteAsync(sql, wallet);
                }
                Console.WriteLine($"[SERVER] Користувач з Id {userId} додав новий гаманець: {wallet.WalletName}");
                await SendPacketAsync(stream, new MessagePacket { Type = MessageType.AddWalletResponse, Data = "Успіх: Гаманець додано!" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Помилка додавання гаманця: {ex.Message}");
                await SendPacketAsync(stream, new MessagePacket { Type = MessageType.Error, Data = "Помилка додавання гаманця!" });
            }
        }


        private static async Task HandleLoadDataAsync(NetworkStream stream, int currentUserId)
        {
            try
            {
                using (IDbConnection db = new SqlConnection(connectionString))
                {
                    // Завантажуємо гаманці ЛИШЕ для поточного користувача за його ID
                    string sql = "SELECT UserId, WalletName, Currency, Balance FROM Wallets WHERE UserId = @UserId";
                    var userWallets = (await db.QueryAsync<Wallet>(sql, new { UserId = currentUserId })).ToList();

                    // Серіалізуємо список гаманців назад клієнту
                    string jsonResponse = JsonSerializer.Serialize(userWallets);
                    await SendPacketAsync(stream, new MessagePacket { Type = MessageType.LoadDataResponse, Data = jsonResponse });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Dapper Error - LoadData]: {ex.Message}");
                await SendPacketAsync(stream, new MessagePacket { Type = MessageType.Error, Data = "Не вдалося завантажити дані гаманців." });
            }
        }


        private static async Task SendPacketAsync(NetworkStream stream, MessagePacket packet)
        {
            string json = JsonSerializer.Serialize(packet);
            byte[] payload = Encoding.UTF8.GetBytes(json);
            byte[] lengthPrefix = BitConverter.GetBytes(payload.Length);

            await stream.WriteAsync(lengthPrefix, 0, 4);
            await stream.WriteAsync(payload, 0, payload.Length);
            await stream.FlushAsync();
        }

        private static async Task<MessagePacket?> ReadMessageAsync(NetworkStream stream)
        {
            try
            {
                byte[] lengthBuffer = new byte[4];
                await stream.ReadExactlyAsync(lengthBuffer, 0, 4);
                int length = BitConverter.ToInt32(lengthBuffer, 0);

                byte[] payloadBuffer = new byte[length];
                await stream.ReadExactlyAsync(payloadBuffer, 0, length);
                string json = Encoding.UTF8.GetString(payloadBuffer);

                return JsonSerializer.Deserialize<MessagePacket>(json);
            }
            catch
            {
                return null; // Обрив зв'язку або закриття сокету
            }
        }
    }
}
