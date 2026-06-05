using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace Team_Project
{
    // клас транзакцій для подальшого складання звітів і тд
    public class Transaction
    {
        public decimal Amount { get; set; }
        public string Category { get; set; }
        public bool IsIncome { get; set; }
        public DateTime Date { get; set; } = DateTime.Now;
    }

    // гаманець користувача
    public class Wallet
    {
        public int UserId { get; set; }
        public string WalletName { get; set; }
        public string Currency { get; set; }
        public decimal Balance { get; set; }

        public List<Transaction> Transactions { get; set; } = new List<Transaction>();
    }

    public partial class MainWindow : Window
    {
        private List<Wallet> wallets = new List<Wallet>();
        private Wallet currentWallet;

        //Заповнення випадаючого списка для вибору типу звіту
        private void FillComboBox()
        {
            ReportChoice.Items.Add("Виберіть тип звіту");
            ReportChoice.Items.Add("Дата");
            ReportChoice.Items.Add("Категорія");
            ReportChoice.SelectedIndex = 0;
        }

        private void FillCardsComboBox()
        {
            Cards.Items.Clear();

            foreach (var wallet in wallets)
            {
                Cards.Items.Add(wallet.WalletName);
            }

            if (Cards.Items.Count > 0)
            {
                Cards.SelectedIndex = 0;
            }
        }

        public MainWindow()
        {
            InitializeComponent();

            FillComboBox();


            Wallet wallet1 = new Wallet
            {
                UserId = 1,
                WalletName = "Mono",
                Currency = "₴",
                Balance = 5800
            };

            Wallet wallet2 = new Wallet
            {
                UserId = 1,
                WalletName = "PayPal",
                Currency = "$",
                Balance = 2500
            };

            // Доходи
            wallet1.Transactions.Add(new Transaction
            {
                Amount = 5000,
                Category = "Перекази з інших карток",
                IsIncome = true,
                Date = DateTime.Now
            });

            wallet1.Transactions.Add(new Transaction
            {
                Amount = 2000,
                Category = "Гроші покладені на картку",
                IsIncome = true,
                Date = DateTime.Now.AddDays(-1)
            });

            wallet1.Transactions.Add(new Transaction
            {
                Amount = 1500,
                Category = "Кешбек",
                IsIncome = true,
                Date = DateTime.Now.AddDays(-2)
            });

            // Витрати
            wallet1.Transactions.Add(new Transaction
            {
                Amount = 1000,
                Category = "Їжа",
                IsIncome = false,
                Date = DateTime.Now
            });

            wallet1.Transactions.Add(new Transaction
            {
                Amount = 500,
                Category = "Транспорт",
                IsIncome = false,
                Date = DateTime.Now.AddDays(-1)
            });

            wallet1.Transactions.Add(new Transaction
            {
                Amount = 1200,
                Category = "Комунальні послуги",
                IsIncome = false,
                Date = DateTime.Now.AddDays(-2)
            });

            wallet2.Transactions.Add(new Transaction
            {
                Amount = 3000,
                Category = "Зарплата",
                IsIncome = true,
                Date = DateTime.Now.AddDays(-1)
            });

            wallet2.Transactions.Add(new Transaction
            {
                Amount = 700,
                Category = "Транспорт",
                IsIncome = false,
                Date = DateTime.Now
            });

            wallet2.Transactions.Add(new Transaction
            {
                Amount = 250,
                Category = "Кава",
                IsIncome = false,
                Date = DateTime.Now
            });

            wallets.Add(wallet1);
            wallets.Add(wallet2);

            FillCardsComboBox();

            currentWallet = wallets[0];
            ShowCurrentBalance();
            ShowLatestTransactions(currentWallet.Transactions);
        }

        //Вибір типу звіту
        private void ReportChoice_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            string choice = ReportChoice.SelectedItem.ToString();
        }

        private void Cards_Choice_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (Cards.SelectedIndex < 0)
                return;

            currentWallet = wallets[Cards.SelectedIndex];

            ShowLatestTransactions(currentWallet.Transactions);
            ShowCurrentBalance();

            IncomeListBox.Items.Clear();
            ExpenseListBox.Items.Clear();
        }
        private decimal GetRate(string from, string to)
        {
            if (from == to)
                return 1m;

            if (from == "₴" && to == "$")
                return 1m / 44m;

            if (from == "$" && to == "₴")
                return 44m;

            if (from == "₴" && to == "€")
                return 1m / 50m;

            if (from == "€" && to == "₴")
                return 50m;

            if (from == "$" && to == "€")
                return 0.87m;

            if (from == "€" && to == "$")
                return 1.15m;

            return 1m;
        }

        private void Transact_Between_Cards_Click(object sender, RoutedEventArgs e)
        {
            TransferCardBox.Items.Clear();

            foreach (var wallet in wallets)
            {
                if (wallet != currentWallet)
                {
                    TransferCardBox.Items.Add(wallet.WalletName);
                }
            }

            TransferCardBox.SelectedIndex = 0;

            TransferCardLabel.Visibility = Visibility.Visible;
            TransferCardBox.Visibility = Visibility.Visible;
            TransferAmountLabel.Visibility = Visibility.Visible;
            TransferAmountBox.Visibility = Visibility.Visible;
            ConfirmTransferButton.Visibility = Visibility.Visible;
        }


        private void ConfirmTransferButton_Click(object sender, RoutedEventArgs e)
        {
            decimal amount;

            if (!decimal.TryParse(
                TransferAmountBox.Text,
                out amount))
            {
                MessageBox.Show("Введіть коректну суму");
                return;
            }

            if (amount <= 0)
            {
                MessageBox.Show("Сума повинна бути більше нуля");
                return;
            }

            if (currentWallet.Balance < amount)
            {
                MessageBox.Show("Недостатньо коштів");
                return;
            }

            Wallet targetWallet = wallets
                .Where(w => w != currentWallet)
                .ElementAt(TransferCardBox.SelectedIndex);

            decimal rate =
                GetRate(
                    currentWallet.Currency,
                    targetWallet.Currency);

            decimal convertedAmount =
                Math.Round(amount * rate, 2);

            currentWallet.Balance -= amount;

            targetWallet.Balance += convertedAmount;

            currentWallet.Transactions.Add(
                new Transaction
                {
                    Amount = amount,
                    Category =
                        $"Переказ на {targetWallet.WalletName}",
                    IsIncome = false,
                    Date = DateTime.Now
                });

            targetWallet.Transactions.Add(
                new Transaction
                {
                    Amount = convertedAmount,
                    Category =
                        $"Переказ з {currentWallet.WalletName}",
                    IsIncome = true,
                    Date = DateTime.Now
                });

            ShowCurrentBalance();
            ShowLatestTransactions(currentWallet.Transactions);

            MessageBox.Show(
                $"Успішний переказ\n" +
                $"{amount} {currentWallet.Currency}\n→\n" +
                $"{convertedAmount} {targetWallet.Currency}");

            TransferAmountBox.Clear();

            TransferCardLabel.Visibility = Visibility.Hidden;
            TransferCardBox.Visibility = Visibility.Hidden;
            TransferAmountLabel.Visibility = Visibility.Hidden;
            TransferAmountBox.Visibility = Visibility.Hidden;
            ConfirmTransferButton.Visibility = Visibility.Hidden;
        }
        private void Make_Report_Click(object sender, RoutedEventArgs e)
        {
            string choice = ReportChoice.SelectedItem.ToString();

            if (choice == "Виберіть тип звіту")
            {
                MessageBox.Show("Оберіть тип звіту");
                return;
            }

            MakeReport(currentWallet.Transactions, choice);
        }
        private void ShowCurrentBalance()
        {
            if (currentWallet == null)
                return;

            CurrentBalanceLabel.Content =
                $"{currentWallet.Balance} {currentWallet.Currency}";
        }
        public void ShowLatestTransactions(List<Transaction> transactions)
        {
            LatestTransactionsListBox.Items.Clear();

            var latestTransactions = transactions
                .OrderByDescending(t => t.Date)
                .Take(10);

            foreach (var transaction in latestTransactions)
            {
                string type = transaction.IsIncome ? "Дохід" : "Витрата";

                LatestTransactionsListBox.Items.Add(
                    $"{transaction.Date.ToShortDateString()} - {type}: {transaction.Category} - {transaction.Amount} {currentWallet.Currency}");
            }
        }

        //Створення звіту на основі вибору користувача
        public void MakeReport(List<Transaction> transactions, string choice)
        {
            decimal totalIncome = 0;
            decimal totalExpense = 0;

            IncomeListBox.Items.Clear();
            ExpenseListBox.Items.Clear();

            if (choice == "Дата")
            {
                var groupedByDate = transactions
                    .GroupBy(t => t.Date.Date)
                    .OrderBy(g => g.Key)
                    .Select(g => new
                    {
                        Date = g.Key,
                        IncomeTransactions = g.Where(t => t.IsIncome).ToList(),
                        ExpenseTransactions = g.Where(t => !t.IsIncome).ToList()
                    });

                foreach (var group in groupedByDate)
                {
                    IncomeListBox.Items.Add($"----- {group.Date.ToShortDateString()} -----");
                    foreach (var income in group.IncomeTransactions)
                    {
                        IncomeListBox.Items.Add(
                            $"{income.Category}: +{income.Amount} {currentWallet.Currency}");

                        totalIncome += income.Amount;
                    }

                    ExpenseListBox.Items.Add($"----- {group.Date.ToShortDateString()} -----");

                    foreach (var expense in group.ExpenseTransactions)
                    {
                        ExpenseListBox.Items.Add(
                            $"{expense.Category}: -{expense.Amount} {currentWallet.Currency}");

                        totalExpense += expense.Amount;
                    }
                }
            }

            // Виведення результатів для кожної категорії

            else if (choice == "Категорія")
            {
                var incomeGroups = transactions
                    .Where(t => t.IsIncome)
                    .GroupBy(t => t.Category);

                foreach (var group in incomeGroups)
                {
                    decimal sum = group.Sum(t => t.Amount);

                    IncomeListBox.Items.Add(
                        $"{group.Key}: +{sum} {currentWallet.Currency}");

                    totalIncome += sum;
                }

                var expenseGroups = transactions
                    .Where(t => !t.IsIncome)
                    .GroupBy(t => t.Category);

                foreach (var group in expenseGroups)
                {
                    decimal sum = group.Sum(t => t.Amount);

                    ExpenseListBox.Items.Add(
                        $"{group.Key}: -{sum} {currentWallet.Currency}");

                    totalExpense += sum;
                }
            }

            IncomeListBox.Items.Add("--------------------------------");
            IncomeListBox.Items.Add(
                $"Загальний дохід: +{totalIncome} {currentWallet.Currency}");

            ExpenseListBox.Items.Add("--------------------------------");
            ExpenseListBox.Items.Add(
                $"Загальні витрати: -{totalExpense} {currentWallet.Currency}");
        }
    }
}