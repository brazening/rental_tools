using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Npgsql;

namespace Rental
{
    public partial class Return : Window
    {
        private string connString;
        private ObservableCollection<RentalItem> rentalsList;
        private ObservableCollection<ToolItem> toolsList;
        private int selectedRentalId = -1;
        private DateTime startDate;
        private DateTime endDate;

        public class RentalItem
        {
            public int Id { get; set; }
            public string Text { get; set; }
            public DateTime EndDate { get; set; }
            public DateTime StartDate { get; set; }
        }

        public class ToolItem : INotifyPropertyChanged
        {
            private bool _isProcessed = false;
            private string _status = "";
            private decimal _fine = 0;
            private decimal _repairCost = 0;
            private string _repairDescription = "";
            private int _selectedEmployeeId = 0;

            public int Id { get; set; }
            public int ToolId { get; set; }
            public string InventoryNumber { get; set; }
            public string ModelName { get; set; }
            public decimal PricePerDay { get; set; }
            public decimal? PurchasePrice { get; set; }
            public decimal OverdueFine { get; set; }

            // Сотрудник, отправивший на ремонт
            public int SelectedEmployeeId
            {
                get => _selectedEmployeeId;
                set
                {
                    _selectedEmployeeId = value;
                    OnPropertyChanged(nameof(SelectedEmployeeId));
                }
            }

            public bool IsProcessed
            {
                get => _isProcessed;
                set
                {
                    _isProcessed = value;
                    OnPropertyChanged(nameof(IsProcessed));
                    OnPropertyChanged(nameof(CanReturn));
                    OnPropertyChanged(nameof(ReturnButtonColor));
                    OnPropertyChanged(nameof(BrokenButtonColor));
                    OnPropertyChanged(nameof(LostButtonColor));
                }
            }

            public string Status
            {
                get => _status;
                set
                {
                    _status = value;
                    OnPropertyChanged(nameof(Status));
                }
            }

            public decimal RepairCost
            {
                get => _repairCost;
                set
                {
                    _repairCost = value;
                    OnPropertyChanged(nameof(RepairCost));
                }
            }

            public string RepairDescription
            {
                get => _repairDescription;
                set
                {
                    _repairDescription = value;
                    OnPropertyChanged(nameof(RepairDescription));
                }
            }

            public bool CanReturn => !IsProcessed;

            public Brush ReturnButtonColor => IsProcessed ? Brushes.Gray : Brushes.Green;
            public Brush BrokenButtonColor => IsProcessed ? Brushes.Gray : Brushes.Orange;
            public Brush LostButtonColor => IsProcessed ? Brushes.Gray : Brushes.Red;

            public decimal Fine
            {
                get => _fine;
                set
                {
                    _fine = value;
                    OnPropertyChanged(nameof(Fine));
                }
            }

            public void CalculateFine()
            {
                if (Status == "утерян")
                    Fine = OverdueFine + (PurchasePrice.HasValue ? PurchasePrice.Value : PricePerDay * 100);
                else if (Status == "поломан")
                    Fine = OverdueFine + (RepairCost > 0 ? RepairCost : PricePerDay * 30);
                else
                    Fine = OverdueFine;

                OnPropertyChanged(nameof(Fine));
            }

            public event PropertyChangedEventHandler PropertyChanged;
            protected void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public Return()
        {
            InitializeComponent();
            DataContext = this;

            var app = (App)Application.Current;
            connString = app.connString;

            rentalsList = new ObservableCollection<RentalItem>();
            toolsList = new ObservableCollection<ToolItem>();

            cmbRental.ItemsSource = rentalsList;
            dgTools.ItemsSource = toolsList;

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
                    string query = @"SELECT r.id, c.name, r.end_date, r.start_date 
                                    FROM public.rentals r
                                    LEFT JOIN public.clients c ON r.client_id = c.id
                                    WHERE r.status = 'активна'
                                    ORDER BY r.id DESC";

                    using (var cmd = new NpgsqlCommand(query, conn))
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            rentalsList.Add(new RentalItem
                            {
                                Id = reader.GetInt32(0),
                                Text = $"№{reader.GetInt32(0)} - {reader.GetString(1)}",
                                EndDate = reader.GetDateTime(2),
                                StartDate = reader.GetDateTime(3)
                            });
                        }
                    }
                }

                if (rentalsList.Count == 0)
                {
                    MessageBox.Show("Нет активных договоров аренды!", "Информация", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки договоров: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadTools(int rentalId)
        {
            try
            {
                toolsList.Clear();
                Mouse.OverrideCursor = Cursors.Wait;

                using (var conn = new NpgsqlConnection(connString))
                {
                    conn.Open();

                    // Загружаем даты начала и окончания
                    using (var cmd = new NpgsqlCommand("SELECT start_date, end_date FROM public.rentals WHERE id = @id", conn))
                    {
                        cmd.Parameters.AddWithValue("@id", rentalId);
                        using (var reader = cmd.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                startDate = reader.GetDateTime(0);
                                endDate = reader.GetDateTime(1);
                                txtStartDate.Text = startDate.ToString("dd.MM.yyyy");
                                txtEndDate.Text = endDate.ToString("dd.MM.yyyy");
                            }
                        }
                    }

                    // Загружаем невозвращенные инструменты
                    string query = @"SELECT ri.id, ri.tool_id, t.inventory_number, tm.name, 
                                           ri.price_per_day, t.purchase_price
                                    FROM public.rental_items ri
                                    JOIN public.tools t ON ri.tool_id = t.id
                                    JOIN public.tool_models tm ON t.model_id = tm.id
                                    WHERE ri.rental_id = @id AND ri.return_date IS NULL";

                    using (var cmd = new NpgsqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@id", rentalId);
                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                var tool = new ToolItem
                                {
                                    Id = reader.GetInt32(0),
                                    ToolId = reader.GetInt32(1),
                                    InventoryNumber = reader.GetString(2),
                                    ModelName = reader.GetString(3),
                                    PricePerDay = reader.GetDecimal(4),
                                    PurchasePrice = reader.IsDBNull(5) ? (decimal?)null : reader.GetDecimal(5),
                                    IsProcessed = false,
                                    Status = "",
                                    RepairCost = 0,
                                    RepairDescription = ""
                                };

                                toolsList.Add(tool);
                            }
                        }
                    }
                }

                if (toolsList.Count == 0)
                {
                    MessageBox.Show("По данному договору нет инструментов для возврата!", "Информация", MessageBoxButton.OK, MessageBoxImage.Information);
                }

                CalculateOverdue();
                UpdateTotalFine();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки инструментов: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }

        private void CalculateOverdue()
        {
            int overdueDays = DateTime.Now > endDate ? (DateTime.Now - endDate).Days : 0;
            decimal totalOverdueFine = 0;

            txtOverdueDays.Text = $"{overdueDays} дн.";
            txtOverdueDays.Foreground = overdueDays > 0 ? Brushes.Red : Brushes.Green;

            foreach (var item in toolsList)
            {
                if (!item.IsProcessed)
                {
                    item.OverdueFine = overdueDays * item.PricePerDay;
                }
                else
                {
                    item.OverdueFine = 0;
                }
                totalOverdueFine += item.OverdueFine;
                item.CalculateFine();
            }

            txtOverdueFine.Text = $"{totalOverdueFine:N2} ₽";
        }

        private void UpdateTotalFine()
        {
            decimal total = toolsList.Sum(item => item.Fine);
            int processedCount = toolsList.Count(item => item.IsProcessed);
            bool allProcessed = toolsList.Count > 0 && processedCount == toolsList.Count;

            txtTotalFine.Text = $"{total:N2}";
            btnReturn.IsEnabled = allProcessed;
            btnReturn.Background = allProcessed ? Brushes.Green : Brushes.Gray;
        }

        private void CmbRental_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cmbRental.SelectedItem is RentalItem selected)
            {
                selectedRentalId = selected.Id;
                LoadClientInfo(selectedRentalId);
                LoadTools(selectedRentalId);
            }
        }

        private void LoadClientInfo(int rentalId)
        {
            try
            {
                using (var conn = new NpgsqlConnection(connString))
                {
                    conn.Open();
                    using (var cmd = new NpgsqlCommand("SELECT c.name, c.phone FROM public.rentals r LEFT JOIN public.clients c ON r.client_id = c.id WHERE r.id = @id", conn))
                    {
                        cmd.Parameters.AddWithValue("@id", rentalId);
                        using (var reader = cmd.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                txtClient.Text = reader.IsDBNull(0) ? "Не указан" : reader.GetString(0);
                                txtPhone.Text = reader.IsDBNull(1) ? "Не указан" : reader.GetString(1);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки клиента: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            LoadRentals();
            if (selectedRentalId != -1)
            {
                LoadClientInfo(selectedRentalId);
                LoadTools(selectedRentalId);
            }
        }

        private void BtnReturned_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var item = button?.Tag as ToolItem;

            if (item == null || item.IsProcessed) return;

            item.Status = "возвращен";
            item.CalculateFine();

            ProcessReturn(item, false, false, 0, "", 0);
        }


        private void BtnLost_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var item = button?.Tag as ToolItem;

            if (item == null || item.IsProcessed) return;

            decimal lostFine = item.PurchasePrice.HasValue ? item.PurchasePrice.Value : item.PricePerDay * 100;
            item.Status = "утерян";
            item.CalculateFine();

            ProcessReturn(item, true, false, lostFine, "", 0);
        }

        private void BtnBroken_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var item = button?.Tag as ToolItem;

            if (item == null || item.IsProcessed) return;

            // Диалог ввода стоимости ремонта, описания и выбора сотрудника
            var dialog = new RepairDialog();
            if (dialog.ShowDialog() == true)
            {
                item.Status = "поломан";
                item.RepairCost = dialog.RepairCost;
                item.RepairDescription = dialog.RepairDescription;
                item.SelectedEmployeeId = dialog.SelectedEmployeeId;
                item.CalculateFine();

                ProcessReturn(item, false, true, dialog.RepairCost, dialog.RepairDescription, dialog.SelectedEmployeeId);
            }
        }

        private void ProcessReturn(ToolItem item, bool isLost, bool isBroken, decimal repairCost, string repairDescription, int employeeId)
        {
            using (var conn = new NpgsqlConnection(connString))
            {
                conn.Open();
                using (var transaction = conn.BeginTransaction())
                {
                    try
                    {
                        string status = isLost ? "утерян" : (isBroken ? "в ремонте" : "доступен");
                        string conditionStatus = isLost ? "утерян" : (isBroken ? "требует ремонта" : "хорошее");

                        // Обновляем инструмент
                        using (var cmd = new NpgsqlCommand("UPDATE public.tools SET status = @status, condition_status = @condition WHERE id = @id", conn, transaction))
                        {
                            cmd.Parameters.AddWithValue("@status", status);
                            cmd.Parameters.AddWithValue("@condition", conditionStatus);
                            cmd.Parameters.AddWithValue("@id", item.ToolId);
                            cmd.ExecuteNonQuery();
                        }

                        // Обновляем запись аренды
                        using (var cmd = new NpgsqlCommand("UPDATE public.rental_items SET return_date = @date, is_damaged = @damaged WHERE id = @id", conn, transaction))
                        {
                            cmd.Parameters.AddWithValue("@date", DateTime.Now);
                            cmd.Parameters.AddWithValue("@damaged", isBroken);
                            cmd.Parameters.AddWithValue("@id", item.Id);
                            cmd.ExecuteNonQuery();
                        }

                        // Если поломан - добавляем в ремонт с указанием сотрудника
                        if (isBroken)
                        {
                            using (var cmd = new NpgsqlCommand(@"INSERT INTO public.repairs 
                        (tool_id, issue_description, status, cost, start_date, employee_id) 
                        VALUES (@tool_id, @description, 'в процессе', @cost, @date, @employee_id)", conn, transaction))
                            {
                                cmd.Parameters.AddWithValue("@tool_id", item.ToolId);
                                cmd.Parameters.AddWithValue("@description", repairDescription);
                                cmd.Parameters.AddWithValue("@cost", repairCost > 0 ? repairCost : (object)DBNull.Value);
                                cmd.Parameters.AddWithValue("@date", DateTime.Now);
                                cmd.Parameters.AddWithValue("@employee_id", employeeId > 0 ? (object)employeeId : DBNull.Value);
                                cmd.ExecuteNonQuery();
                            }
                        }

                        // Штраф за поломку
                        if (isBroken && repairCost > 0)
                        {
                            using (var cmd = new NpgsqlCommand("INSERT INTO public.fines (rental_id, type, amount, created_at) VALUES (@id, 'повреждение', @amount, @date)", conn, transaction))
                            {
                                cmd.Parameters.AddWithValue("@id", selectedRentalId);
                                cmd.Parameters.AddWithValue("@amount", repairCost);
                                cmd.Parameters.AddWithValue("@date", DateTime.Now);
                                cmd.ExecuteNonQuery();
                            }
                        }

                        // Штраф за потерю
                        if (isLost)
                        {
                            using (var cmd = new NpgsqlCommand("INSERT INTO public.fines (rental_id, type, amount, created_at) VALUES (@id, 'потеря', @amount, @date)", conn, transaction))
                            {
                                cmd.Parameters.AddWithValue("@id", selectedRentalId);
                                cmd.Parameters.AddWithValue("@amount", repairCost);
                                cmd.Parameters.AddWithValue("@date", DateTime.Now);
                                cmd.ExecuteNonQuery();
                            }
                        }

                        transaction.Commit();

                        item.IsProcessed = true;
                        UpdateTotalFine();

                        // Показываем сообщение
                        if (item.Fine > item.OverdueFine)
                        {
                            MessageBox.Show($"Инструмент {item.InventoryNumber} обработан!\n" +
                                           $"Штраф: {(item.Fine - item.OverdueFine):N2} ₽\n" +
                                           $"Сотрудник ID: {(employeeId > 0 ? employeeId.ToString() : "не указан")}",
                                           "Информация", MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                    }
                    catch (Exception ex)
                    {
                        transaction.Rollback();
                        MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        private void BtnCalcAll_Click(object sender, RoutedEventArgs e)
        {
            CalculateOverdue();
            foreach (var item in toolsList) item.CalculateFine();
            UpdateTotalFine();
            MessageBox.Show("Штрафы пересчитаны!", "Готово", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnReturn_Click(object sender, RoutedEventArgs e)
        {
            if (selectedRentalId == -1)
            {
                MessageBox.Show("Выберите договор!", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (MessageBox.Show("Завершить возврат по договору?\nПосле завершения изменения будут невозможны!", "Подтверждение", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            try
            {
                using (var conn = new NpgsqlConnection(connString))
                {
                    conn.Open();
                    using (var transaction = conn.BeginTransaction())
                    {
                        decimal totalFines = toolsList.Sum(item => item.Fine);

                        // Штраф за просрочку
                        if (decimal.TryParse(txtOverdueFine.Text.Replace(" ₽", ""), out decimal overdueFine) && overdueFine > 0)
                        {
                            using (var cmd = new NpgsqlCommand("SELECT COUNT(*) FROM public.fines WHERE rental_id = @id AND type = 'просрочка'", conn, transaction))
                            {
                                cmd.Parameters.AddWithValue("@id", selectedRentalId);
                                if ((long)cmd.ExecuteScalar() == 0)
                                {
                                    using (var cmdFine = new NpgsqlCommand("INSERT INTO public.fines (rental_id, type, amount, created_at) VALUES (@id, 'просрочка', @amount, @date)", conn, transaction))
                                    {
                                        cmdFine.Parameters.AddWithValue("@id", selectedRentalId);
                                        cmdFine.Parameters.AddWithValue("@amount", overdueFine);
                                        cmdFine.Parameters.AddWithValue("@date", DateTime.Now);
                                        cmdFine.ExecuteNonQuery();
                                    }
                                }
                            }
                        }

                        // Закрываем договор
                        using (var cmd = new NpgsqlCommand("UPDATE public.rentals SET status = 'завершена', actual_return_date = @date, total_price = total_price + @fines WHERE id = @id", conn, transaction))
                        {
                            cmd.Parameters.AddWithValue("@date", DateTime.Now);
                            cmd.Parameters.AddWithValue("@fines", totalFines);
                            cmd.Parameters.AddWithValue("@id", selectedRentalId);
                            cmd.ExecuteNonQuery();
                        }

                        transaction.Commit();

                        MessageBox.Show($"Договор №{selectedRentalId} закрыт!\nОбщая сумма штрафов: {totalFines:N2} ₽", "Успех", MessageBoxButton.OK, MessageBoxImage.Information);

                        // Очистка формы
                        LoadRentals();
                        toolsList.Clear();
                        cmbRental.SelectedItem = null;
                        txtClient.Text = txtPhone.Text = txtStartDate.Text = txtEndDate.Text = "";
                        txtOverdueDays.Text = "0 дн.";
                        txtOverdueFine.Text = "0 ₽";
                        txtTotalFine.Text = "0";
                        btnReturn.IsEnabled = false;
                        selectedRentalId = -1;
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}