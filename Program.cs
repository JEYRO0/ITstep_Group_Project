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
        public int Id { get; set; }
        public string NickName { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }

    public class Wallet
    {
        public int UserId { get; set; }
        public string WalletName { get; set; } = string.Empty;
        public string Currency { get; set; } = string.Empty;
        public decimal Balance { get; set; }
    }

    public class Transaction
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public string WalletName { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string Category { get; set; } = string.Empty;
        public bool IsIncome { get; set; }
        public DateTime Date { get; set; } = DateTime.Now;

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
        AddTransactionRequest,
        AddTransactionResponse,
        LoadTransactionsRequest,
        LoadTransactionsResponse,
        DeleteTransactionRequest,
        DeleteTransactionResponse,
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

        // Захист від гонки потоків в сокеті
        private static readonly SemaphoreSlim _networkSemaphore = new SemaphoreSlim(1, 1);


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
                            case MessageType.AddTransactionRequest:
                                if (authenticatedUser == null)
                                {
                                    await SendPacketAsync(stream, new MessagePacket { Type = MessageType.Error, Data = "Ви не авторизовані!" });
                                    break;
                                }
                                await HandleAddTransactionAsync(stream, authenticatedUser.Id, packet.Data);
                                break;
                            case MessageType.LoadTransactionsRequest:
                                if (authenticatedUser == null)
                                {
                                    await SendPacketAsync(stream, new MessagePacket { Type = MessageType.Error, Data = "Ви не авторизовані!" });
                                    break;
                                }
                                await HandleLoadTransactionsAsync(stream, authenticatedUser.Id);
                                break;
                            case MessageType.DeleteTransactionRequest:
                                if (authenticatedUser == null)
                                {
                                    await SendPacketAsync(stream, new MessagePacket { Type = MessageType.Error, Data = "Ви не авторизовані!" });
                                    break;
                                }
                                await HandleDeleteTransactionAsync(stream, authenticatedUser.Id, packet.Data);
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
                finally
                {
                    if (authenticatedUser != null)
                    {
                        _authenticatedClients.TryRemove(authenticatedUser.Id, out _);
                        Console.WriteLine($"[SERVER] Користувач {authenticatedUser.NickName} відключився.");
                    }
                }
            }

        }

        private static async Task HandleRegisterAsync(NetworkStream stream, string data)
        {

            try
            {
                // Десеріалізуємо дані користувача з отриманого JSON
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
                // Десеріалізуємо дані користувача з отриманого JSON
                var loginData = JsonSerializer.Deserialize<User>(data);
                if(loginData == null) return null;

                using (IDbConnection db = new SqlConnection(connectionString))
                {
                    // Шукаємо користувача в базі даних за нікнеймом та паролем
                    string sql = "SELECT Id, NickName, Password FROM Users WHERE NickName = @NickName AND Password = @Password";
                    var user = await db.QueryFirstOrDefaultAsync<User>(sql, loginData);
                    // Якщо користувача не знайдено або пароль не співпадає — відправляємо помилку
                    if (user == null)
                    {
                        await SendPacketAsync(stream, new MessagePacket { Type = MessageType.LoginResponse, Data = "Помилка: Невірний нікнейм або пароль!" });
                        return null;
                    }

                    // Перевіряємо, чи цей користувач вже авторизований в іншому місці
                    if (_authenticatedClients.ContainsKey(user.Id))
                    {
                        await SendPacketAsync(stream, new MessagePacket { Type = MessageType.LoginResponse, Data = "Помилка: Цей користувач вже авторизований!" });
                        return null;
                    }

                    // Якщо все добре — додаємо його в список авторизованих клієнтів
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
                // Десеріалізуємо дані гаманця з отриманого JSON
                var wallet = JsonSerializer.Deserialize<Wallet>(data);
                if (wallet == null) return;

                // Прив'язуємо гаманець до поточного користувача
                wallet.UserId = userId;
                using (IDbConnection db = new SqlConnection(connectionString))
                {
                    // Зберігаємо гаманець в базу даних
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


        private static async Task HandleAddTransactionAsync(NetworkStream stream, int userId, string data)
        {
            try
            {
                // Десеріалізуємо дані транзакції з отриманого JSON
                var transaction = JsonSerializer.Deserialize<Transaction>(data);
                if (transaction == null) return;
                transaction.UserId = userId;
                using (SqlConnection db = new SqlConnection(connectionString))
                {
                    // Відкриваємо з'єднання та починаємо транзакцію для забезпечення цілісності даних
                    await db.OpenAsync();

                    using (var sqlTx = db.BeginTransaction())
                    {
                        // Спочатку додаємо транзакцію в базу даних
                        string sql = @"INSERT INTO Transactions (UserId, WalletName, Amount, Category, IsIncome, Date) 
                                   VALUES (@UserId, @WalletName, @Amount, @Category, @IsIncome, @Date);";
                        await db.ExecuteAsync(sql, transaction, transaction : sqlTx);

                        // Потім оновлюємо баланс відповідного гаманця, враховуючи тип транзакції (дохід чи витрата)
                        string updateWalletSql = transaction.IsIncome
                                    ? "UPDATE Wallets SET Balance = Balance + @Amount WHERE UserId = @UserId AND WalletName = @WalletName"
                                    : "UPDATE Wallets SET Balance = Balance - @Amount WHERE UserId = @UserId AND WalletName = @WalletName";

                        int rowsAffected = await db.ExecuteAsync(updateWalletSql, new
                        {
                            Amount = transaction.Amount,
                            UserId = userId,
                            WalletName = transaction.WalletName
                        }, transaction: sqlTx);

                        if (rowsAffected == 0)
                        {
                            // Якщо гаманець з такою назвою не знайдено — скасовуємо зміни
                            sqlTx.Rollback();
                            await SendPacketAsync(stream, new MessagePacket { Type = MessageType.Error, Data = "Помилка: Гаманець не знайдено!" });
                            return;
                        }

                        // Якщо все пройшло успішно — зберігаємо зміни в базі фіксовано
                        sqlTx.Commit();
                        try { sqlTx.Rollback(); } catch { /* ігноруємо помилки відкату, якщо з'єднання втрачено */ }
                    }
                }
                Console.WriteLine($"[SERVER] Користувач з Id {userId} додав нову транзакцію: {transaction.Amount} в категорії {transaction.Category}");
                await SendPacketAsync(stream, new MessagePacket { Type = MessageType.AddTransactionResponse, Data = "Успіх: Транзакцію додано!" });
            }
            catch (Exception ex)
            {
                
                Console.WriteLine($"[ERROR] Помилка додавання транзакції: {ex.Message}");
                await SendPacketAsync(stream, new MessagePacket { Type = MessageType.Error, Data = "Помилка додавання транзакції!" });
            }
        }

        private static async Task HandleLoadTransactionsAsync(NetworkStream stream, int userId)
        {
            try
            {
                using (IDbConnection db = new SqlConnection(connectionString))
                {
                    // Завантажуємо транзакції ЛИШЕ для поточного користувача за його ID
                    string sql = "SELECT Id, UserId, WalletName, Amount, Category, IsIncome, Date FROM Transactions WHERE UserId = @UserId ORDER BY Date DESC";
                    var userTransactions = (await db.QueryAsync<Transaction>(sql, new { UserId = userId })).ToList();

                    // Серіалізуємо список транзакцій назад клієнту
                    string jsonResponse = JsonSerializer.Serialize(userTransactions);
                    await SendPacketAsync(stream, new MessagePacket { Type = MessageType.LoadTransactionsResponse, Data = jsonResponse });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Dapper Error - LoadTransactions]: {ex.Message}");
                await SendPacketAsync(stream, new MessagePacket { Type = MessageType.Error, Data = "Не вдалося завантажити дані транзакцій." });
            }
        }

        private static async Task HandleDeleteTransactionAsync(NetworkStream stream, int userId, string data)
        {
            try
            {
                // Десеріалізуємо ID транзакції з отриманого JSON
                if (!int.TryParse(data, out int transactionId))
                {
                    await SendPacketAsync(stream, new MessagePacket { Type = MessageType.Error, Data = "Помилка: Некоректний ID транзакції!" });
                    return;
                }
                using (SqlConnection db = new SqlConnection(connectionString))
                {
                    await db.OpenAsync();
                    using (var sqlTx = db.BeginTransaction())
                    {
                        try
                        {
                            // Спочатку отримуємо інформацію про транзакцію, щоб знати, на який гаманець і на яку суму вплинути при видаленні
                            string getTxSql = "SELECT WalletName, Amount, IsIncome FROM Transactions WHERE Id = @Id AND UserId = @UserId";
                            var tx = await db.QueryFirstOrDefaultAsync<Transaction>(getTxSql, new { Id = transactionId, UserId = userId }, transaction: sqlTx);
                            if (tx == null)
                            {
                                // Якщо транзакцію не знайдено — скасовуємо зміни
                                sqlTx.Rollback();
                                await SendPacketAsync(stream, new MessagePacket { Type = MessageType.Error, Data = "Помилка: Транзакцію не знайдено!" });
                                return;
                            }
                            // Потім видаляємо транзакцію з бази даних
                            string deleteSql = "DELETE FROM Transactions WHERE Id = @Id AND UserId = @UserId";
                            await db.ExecuteAsync(deleteSql, new { Id = transactionId, UserId = userId }, transaction: sqlTx);
                            // І нарешті оновлюємо баланс відповідного гаманця, враховуючи тип транзакції (дохід чи витрата) — при видаленні доходу баланс зменшується, при видаленні витрати — збільшується
                            string updateWalletSql = tx.IsIncome
                                        ? "UPDATE Wallets SET Balance = Balance - @Amount WHERE UserId = @UserId AND WalletName = @WalletName"
                                        : "UPDATE Wallets SET Balance = Balance + @Amount WHERE UserId = @UserId AND WalletName = @WalletName";
                            int rowsAffected = await db.ExecuteAsync(updateWalletSql, new
                            {
                                Amount = tx.Amount,
                                UserId = userId,
                                WalletName = tx.WalletName
                            }, transaction: sqlTx);
                            // Якщо гаманець з такою назвою не знайдено — скасовуємо зміни
                            if (rowsAffected == 0)
                            {
                                sqlTx.Rollback();
                                await SendPacketAsync(stream, new MessagePacket { Type = MessageType.Error, Data = "Помилка: Гаманець не знайдено!" });
                                return;
                            }
                            sqlTx.Commit();
                        }
                        catch (Exception ex) {
                            try { sqlTx.Rollback(); } catch { /* ігноруємо помилки відкату */ }
                            throw; // прокидаємо помилку далі у зовнішній catch для логування
                        }
                        
                    }
                }
                Console.WriteLine($"[SERVER] Користувач з Id {userId} видалив транзакцію з Id {transactionId}");
                await SendPacketAsync(stream, new MessagePacket
                {
                    Type = MessageType.DeleteTransactionResponse,
                    Data = "Успіх: Транзакцію видалено!"
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Помилка видалення транзакції: {ex.Message}");
                await SendPacketAsync(stream, new MessagePacket { Type = MessageType.Error, Data = "Помилка видалення транзакції!" });
            }
        }

        private static async Task SendPacketAsync(NetworkStream stream, MessagePacket packet)
        {
            string json = JsonSerializer.Serialize(packet);
            byte[] payload = Encoding.UTF8.GetBytes(json);
            byte[] lengthPrefix = BitConverter.GetBytes(payload.Length);



            await _networkSemaphore.WaitAsync();
            try
            {
                await stream.WriteAsync(lengthPrefix, 0, 4);
                await stream.WriteAsync(payload, 0, payload.Length);
                await stream.FlushAsync();
            }
            finally
            {
                _networkSemaphore.Release();
            }
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
