using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using Npgsql;

namespace Rental
{
    public partial class RepairDialog : Window
    {
        public decimal RepairCost { get; private set; }
        public string RepairDescription { get; private set; }
        public int SelectedEmployeeId { get; private set; }
        public DateTime RepairStartDate { get; private set; }

        private string _connectionString;

        public class EmployeeItem
        {
            public int Id { get; set; }
            public string FullName { get; set; }
            public string Role { get; set; }

            public override string ToString()
            {
                return string.IsNullOrEmpty(Role) ? FullName : $"{FullName} ({Role})";
            }
        }

        public RepairDialog()
        {
            InitializeComponent();

            _connectionString = ((App)Application.Current).connString;
            dpStartDate.SelectedDate = DateTime.Today;

            LoadEmployees();
        }

        private void LoadEmployees()
        {
            try
            {
                var employees = new List<EmployeeItem>();

                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    conn.Open();
                    string query = @"SELECT id, fio, role FROM public.employees ORDER BY fio";

                    using (var cmd = new NpgsqlCommand(query, conn))
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            employees.Add(new EmployeeItem
                            {
                                Id = reader.GetInt32(0),
                                FullName = reader.GetString(1),
                                Role = reader.IsDBNull(2) ? null : reader.GetString(2)
                            });
                        }
                    }
                }

                cmbEmployees.ItemsSource = employees;
                cmbEmployees.DisplayMemberPath = "FullName";
                cmbEmployees.SelectedValuePath = "Id";

                if (employees.Any())
                {
                    cmbEmployees.SelectedIndex = 0;
                    SelectedEmployeeId = employees[0].Id;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки сотрудников: {ex.Message}",
                    "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void NumberValidationTextBox(object sender, TextCompositionEventArgs e)
        {
            Regex regex = new Regex("[^0-9.,]");
            e.Handled = regex.IsMatch(e.Text);
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (dpStartDate.SelectedDate == null)
            {
                MessageBox.Show("Выберите дату начала ремонта!", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                dpStartDate.Focus();
                return;
            }

            if (cmbEmployees.SelectedItem == null)
            {
                MessageBox.Show("Выберите сотрудника, отправившего инструмент на ремонт!", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                cmbEmployees.Focus();
                return;
            }

            RepairStartDate = dpStartDate.SelectedDate.Value;
            SelectedEmployeeId = ((EmployeeItem)cmbEmployees.SelectedItem).Id;
            RepairDescription = string.IsNullOrWhiteSpace(txtDescription.Text)
                ? "Повреждение инструмента"
                : txtDescription.Text.Trim();

            if (!decimal.TryParse(txtRepairCost.Text, out decimal cost))
                cost = 0;
            RepairCost = cost;

            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}