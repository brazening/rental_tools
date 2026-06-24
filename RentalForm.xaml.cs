using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Npgsql;

namespace Rental
{
    public partial class RentalForm : Window
    {
        private string connString;
        private ObservableCollection<Rental> rentalsList;
        private int selectedRentalId = -1;

        public class Rental
        {
            public int Id { get; set; }
            public int? ClientId { get; set; }
            public string ClientName { get; set; }
            public int? EmployeeId { get; set; }
            public string EmployeeFIO { get; set; }
            public DateTime StartDate { get; set; }
            public DateTime EndDate { get; set; }
            public DateTime? ActualReturnDate { get; set; }
            public string Status { get; set; }
            public decimal? TotalPrice { get; set; }
            public string ToolsSummary { get; set; }
        }

        public RentalForm()
        {
            InitializeComponent();

            var app = (App)Application.Current;
            connString = app.connString;

            rentalsList = new ObservableCollection<Rental>();
            dgRentals.ItemsSource = rentalsList;

            LoadRentals();
        }

        private void LoadRentals()
        {
            try
            {
                rentalsList.Clear();

                using (var conn = new NpgsqlConnection(connString))
                {
                    conn.Open();
                    string query = @"SELECT 
                                        r.id,
                                        r.client_id,
                                        COALESCE(c.name, '') as client_name,
                                        r.employee_id,
                                        COALESCE(e.fio, '') as employee_fio,
                                        r.start_date,
                                        r.end_date,
                                        r.actual_return_date,
                                        r.status,
                                        r.total_price,
                                        COALESCE((
                                            SELECT STRING_AGG(DISTINCT t.inventory_number, ', ')
                                            FROM public.rental_items ri
                                            JOIN public.tools t ON ri.tool_id = t.id
                                            WHERE ri.rental_id = r.id
                                        ), 'Нет инструментов') as tools_summary
                                    FROM public.rentals r
                                    LEFT JOIN public.clients c ON r.client_id = c.id
                                    LEFT JOIN public.employees e ON r.employee_id = e.id
                                    ORDER BY r.id DESC";

                    using (var cmd = new NpgsqlCommand(query, conn))
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            rentalsList.Add(new Rental
                            {
                                Id = reader.GetInt32(0),
                                ClientId = reader.IsDBNull(1) ? (int?)null : reader.GetInt32(1),
                                ClientName = reader.GetString(2),
                                EmployeeId = reader.IsDBNull(3) ? (int?)null : reader.GetInt32(3),
                                EmployeeFIO = reader.GetString(4),
                                StartDate = reader.GetDateTime(5),
                                EndDate = reader.GetDateTime(6),
                                ActualReturnDate = reader.IsDBNull(7) ? (DateTime?)null : reader.GetDateTime(7),
                                Status = reader.GetString(8),
                                TotalPrice = reader.IsDBNull(9) ? (decimal?)null : reader.GetDecimal(9),
                                ToolsSummary = reader.GetString(10)
                            });
                        }
                    }
                }

                txtStatus.Text = $"✅ Загружено {rentalsList.Count} договоров";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                txtStatus.Text = "❌ Ошибка загрузки";
            }
        }

        private void btnAdd_Click(object sender, RoutedEventArgs e)
        {
            var editForm = new RentalEditForm();
            editForm.Owner = this;
            if (editForm.ShowDialog() == true)
            {
                LoadRentals();
                txtStatus.Text = "✅ Договор успешно добавлен";
            }
        }

        private void btnEdit_Click(object sender, RoutedEventArgs e)
        {
            if (selectedRentalId == -1)
            {
                MessageBox.Show("Выберите договор для редактирования!", "Предупреждение",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var editForm = new RentalEditForm(selectedRentalId);
            editForm.Owner = this;
            if (editForm.ShowDialog() == true)
            {
                LoadRentals();
                txtStatus.Text = "✅ Договор успешно обновлен";
            }
        }

        private void btnView_Click(object sender, RoutedEventArgs e)
        {
            if (selectedRentalId == -1)
            {
                MessageBox.Show("Выберите договор для просмотра!", "Предупреждение",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var viewForm = new RentalViewForm(selectedRentalId);
            viewForm.Owner = this;
            viewForm.ShowDialog();
        }

        private void btnRefresh_Click(object sender, RoutedEventArgs e)
        {
            LoadRentals();
        }

        private void dgRentals_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (dgRentals.SelectedItem is Rental selectedRental)
            {
                selectedRentalId = selectedRental.Id;
                btnEdit.IsEnabled = true;
                btnView.IsEnabled = true;
                txtStatus.Text = $"📌 Выбран договор №{selectedRental.Id} - {selectedRental.ClientName}";
            }
            else
            {
                selectedRentalId = -1;
                btnEdit.IsEnabled = false;
                btnView.IsEnabled = false;
            }
        }
    }
}