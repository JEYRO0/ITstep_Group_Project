using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace Team_Project
{
    // клас транзакцій для  подальшого складання звітів і тд
    public class Transaction
    {
        public decimal Amount { get; set; }
        public string Category { get; set; }
        public bool IsIncome { get; set; }
        public DateTime Date { get; set; } = DateTime.Now;

    }

    public partial class MainWindow : Window
    {
        //Заповнення випадаючого списка для вибору типу звіту
        private void FillComboBox()
        {
            ReportChoice.Items.Add("Виберіть тип звіту");
            ReportChoice.Items.Add("Дата");
            ReportChoice.Items.Add("Категорія");
            ReportChoice.SelectedIndex = 0;
        }

        public MainWindow()
        {
            InitializeComponent();
            FillComboBox();
        }

        //Вибір типу звіту
        private void ReportChoice_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            string choice = ReportChoice.SelectedItem.ToString();
        }

        private void Make_Report_Click(object sender, RoutedEventArgs e)
        {
            List<Transaction> transactions = new List<Transaction>()
            {
                // Доходи
                new Transaction
                {
                    Amount = 5000,
                    Category = "Зарплата",
                    IsIncome = true,
                    Date = DateTime.Now
                },

                new Transaction
                {
                    Amount = 2000,
                    Category = "Фріланс",
                    IsIncome = true,
                    Date = DateTime.Now.AddDays(-1)
                },

                new Transaction
                {
                    Amount = 1500,
                    Category = "Подарунок",
                    IsIncome = true,
                    Date = DateTime.Now.AddDays(-2)
                },

                // Витрати
                new Transaction
                {
                    Amount = 1000,
                    Category = "Їжа",
                    IsIncome = false,
                    Date = DateTime.Now
                },

                new Transaction
                {
                    Amount = 500,
                    Category = "Транспорт",
                    IsIncome = false,
                    Date = DateTime.Now.AddDays(-1)
                },

                new Transaction
                {
                    Amount = 1200,
                    Category = "Комунальні послуги",
                    IsIncome = false,
                    Date = DateTime.Now.AddDays(-2)
                }
            };

            string choice = ReportChoice.SelectedItem.ToString();

            if (choice == "Виберіть тип звіту")
            {
                MessageBox.Show("Оберіть тип звіту!");
                return;
            }

            MakeReport(transactions, choice);
        }

        //Створення звіту на основі вибору користувача
        public void MakeReport(List<Transaction> transactions, string choice)
        {
            decimal totalIncome = 0;
            decimal totalExpense = 0;

            IncomeListBox.Items.Clear();
            ExpenseListBox.Items.Clear();

            // Виведення результатів для кожної дати

            if (choice == "Дата")
            {
                var groupedByDate = transactions.GroupBy(t => t.Date.Date)
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
                            $"{income.Category}: +{income.Amount} грн");

                        totalIncome += income.Amount;
                    }

                    ExpenseListBox.Items.Add($"----- {group.Date.ToShortDateString()} -----");

                    foreach (var expense in group.ExpenseTransactions)
                    {
                        ExpenseListBox.Items.Add(
                            $"{expense.Category}: -{expense.Amount} грн");

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
                        $"{group.Key}: +{sum} грн");

                    totalIncome += sum;
                }

                var expenseGroups = transactions
                    .Where(t => !t.IsIncome)
                    .GroupBy(t => t.Category);

                foreach (var group in expenseGroups)
                {
                    decimal sum = group.Sum(t => t.Amount);

                    ExpenseListBox.Items.Add(
                        $"{group.Key}: -{sum} грн");

                    totalExpense += sum;
                }
            }

            IncomeListBox.Items.Add("--------------------------------");
            IncomeListBox.Items.Add($"Загальний дохід: +{totalIncome} грн");

            ExpenseListBox.Items.Add("--------------------------------");
            ExpenseListBox.Items.Add($"Загальні витрати: -{totalExpense} грн");
        }
    }
}