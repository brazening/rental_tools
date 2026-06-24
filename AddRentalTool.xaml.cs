using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using Npgsql;

namespace Rental
{
    public partial class AddRentalTool : Window
    {
        private string connString;
        private ObservableCollection<ToolComboItem> toolsList;
        private bool isEditMode = false;
        private int? editingToolId = null;

        public class ToolComboItem
        {
            public int Id { get; set; }
            public string DisplayText { get; set; }
            public string InventoryNumber { get; set; }
            public decimal RentalPrice { get; set; }
        }

        // Возвращаемые данные
        public int SelectedToolId { get; private set; }
        public string SelectedToolInventoryNumber { get; private set; }
        public decimal PricePerDay { get; private set; }
        public DateTime? ReturnDate { get; private set; }
        public bool IsDamaged { get; private set; }

        /// <summary>
        /// Конструктор для добавления нового инструмента
        /// </summary>
        /// <param name="showReturnDate">Показывать ли поле даты возврата</param>
        public AddRentalTool(bool showReturnDate = false)
        {
            InitializeComponent();

            var app = (App)Application.Current;
            connString = app.connString;

            toolsList = new ObservableCollection<ToolComboItem>();
            cmbTool.ItemsSource = toolsList;

            // Скрываем поле даты возврата при создании нового договора
            if (!showReturnDate)
            {
                spReturnDate.Visibility = Visibility.Collapsed;
            }

            LoadAvailableTools();
        }

        /// <summary>
        /// Конструктор для редактирования существующего инструмента
        /// </summary>
        /// <param name="toolId">ID инструмента</param>
        /// <param name="pricePerDay">Цена за день</param>
        /// <param name="returnDate">Дата возврата</param>
        /// <param name="isDamaged">Поврежден ли</param>
        public AddRentalTool(int toolId, decimal pricePerDay, DateTime? returnDate, bool isDamaged)
        {
            InitializeComponent();

            var app = (App)Application.Current;
            connString = app.connString;

            isEditMode = true;
            editingToolId = toolId;

            toolsList = new ObservableCollection<ToolComboItem>();
            cmbTool.ItemsSource = toolsList;

            // При редактировании показываем все поля
            spReturnDate.Visibility = Visibility.Visible;
            spDamage.Visibility = Visibility.Visible;

            // Загружаем данные для редактирования
            LoadToolForEdit(toolId, pricePerDay, returnDate, isDamaged);
        }

        private void LoadToolForEdit(int toolId, decimal pricePerDay, DateTime? returnDate, bool isDamaged)
        {
            try
            {
                using (var conn = new NpgsqlConnection(connString))
                {
                    conn.Open();
                    string query = @"SELECT t.id, t.inventory_number, tm.name, tm.rental_price 
                                    FROM public.tools t
                                    JOIN public.tool_models tm ON t.model_id = tm.id
                                    WHERE t.id = @tool_id";

                    using (var cmd = new NpgsqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@tool_id", toolId);
                        using (var reader = cmd.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                var tool = new ToolComboItem
                                {
                                    Id = reader.GetInt32(0),
                                    InventoryNumber = reader.GetString(1),
                                    DisplayText = $"{reader.GetString(1)} - {reader.GetString(2)}",
                                    RentalPrice = reader.GetDecimal(3)
                                };

                                toolsList.Add(tool);
                                cmbTool.SelectedItem = tool;
                                cmbTool.IsEnabled = false; // При редактировании нельзя сменить инструмент
                            }
                        }
                    }
                }

                PricePerDay = pricePerDay;
                txtPricePerDay.Text = pricePerDay.ToString("N2");
                dpReturnDate.SelectedDate = returnDate;
                chkIsDamaged.IsChecked = isDamaged;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки данных инструмента: {ex.Message}", "Ошибка",
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadAvailableTools()
        {
            try
            {
                using (var conn = new NpgsqlConnection(connString))
                {
                    conn.Open();
                    string query = @"SELECT t.id, t.inventory_number, tm.name, tm.rental_price 
                                    FROM public.tools t
                                    JOIN public.tool_models tm ON t.model_id = tm.id
                                    WHERE t.status = 'доступен'
                                    ORDER BY t.inventory_number";

                    using (var cmd = new NpgsqlCommand(query, conn))
                    using (var reader = cmd.ExecuteReader())
                    {
                        toolsList.Clear();
                        while (reader.Read())
                        {
                            toolsList.Add(new ToolComboItem
                            {
                                Id = reader.GetInt32(0),
                                InventoryNumber = reader.GetString(1),
                                DisplayText = $"{reader.GetString(1)} - {reader.GetString(2)}",
                                RentalPrice = reader.GetDecimal(3)
                            });
                        }
                    }
                }

                if (toolsList.Count == 0 && !isEditMode)
                {
                    MessageBox.Show("Нет доступных инструментов для аренды!", "Предупреждение",
                                  MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки инструментов: {ex.Message}", "Ошибка",
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void cmbTool_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cmbTool.SelectedItem is ToolComboItem selectedTool)
            {
                txtPricePerDay.Text = selectedTool.RentalPrice.ToString("N2");

                if (!isEditMode)
                {
                    PricePerDay = selectedTool.RentalPrice;
                }
            }
        }

        private void btnAdd_Click(object sender, RoutedEventArgs e)
        {
            if (!isEditMode && cmbTool.SelectedItem == null)
            {
                MessageBox.Show("Выберите инструмент!", "Ошибка валидации",
                              MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!isEditMode)
            {
                var selectedTool = cmbTool.SelectedItem as ToolComboItem;
                SelectedToolId = selectedTool.Id;
                SelectedToolInventoryNumber = selectedTool.InventoryNumber;
                PricePerDay = selectedTool.RentalPrice;
                ReturnDate = null; // При создании дата возврата всегда null
                IsDamaged = false;
            }
            else
            {
                SelectedToolId = editingToolId.Value;
                SelectedToolInventoryNumber = (cmbTool.SelectedItem as ToolComboItem)?.InventoryNumber;
                PricePerDay = decimal.Parse(txtPricePerDay.Text);
                ReturnDate = dpReturnDate.SelectedDate;
                IsDamaged = chkIsDamaged.IsChecked ?? false;
            }

            DialogResult = true;
            Close();
        }

        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}