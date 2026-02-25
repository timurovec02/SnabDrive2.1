using Microsoft.EntityFrameworkCore;
using SnabDrive2._0.Converters;
using SnabDrive2._0.Models;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Navigation;

namespace SnabDrive2._0.Views.Pages
{
    public partial class RegeditPage : Page
    {
        // Словарь для хранения цветов ячеек: ключ = (Id записи, имя столбца), значение = цвет
        private Dictionary<(int id, string column), string> _cellColors;

        // Текущий выбранный цвет (из ComboBox)
        private string _selectedColorCode = "#FFFF0000"; // красный по умолчанию

        // Для отслеживания выбранной ячейки
        private object _selectedRowItem;
        private string _selectedColumnName;

        

        private DataGrid _currentDataGrid;
        private bool _isArchiveMode = false;
        private readonly SnabDriveDBContext dbContext = new SnabDriveDBContext();

        // Коллекции для хранения всех данных
        private ObservableCollection<Regedit> _allRegedits;
        private ObservableCollection<ArchiveRegedit> _allArchiveRegedits;

        // Отображаемые коллекции (после фильтрации)
        private ObservableCollection<Regedit> _filteredRegedits;
        private ObservableCollection<ArchiveRegedit> _filteredArchiveRegedits;

        // Справочные коллекции
        private ObservableCollection<B2BStatus> B2BStatuses;
        private ObservableCollection<TypeOfPurchase> typeOfPurchases;
        private ObservableCollection<ExecutionStatus> executionStatuses;

        // Таймер для отложенного поиска (debounce)
        private System.Timers.Timer _searchTimer;

        public RegeditPage()
        {
            if (!CurrentUser.IsAuthorized)
                return;
            _cellColors = new Dictionary<(int, string), string>();
            CellColorConverter.CellColors = _cellColors;
            InitializeComponent();
            InitializeCollections();
            SetupSearchDebounce();
            LoadDataAsync();

            
            UserInfo.Visibility = Visibility.Collapsed;
            SettingsBtn.Visibility = Visibility.Collapsed;
            qwe.Content = CurrentUser.Login;
            if (CurrentUser.IsAdmin)
            {
                Admin.Content = "Админ";
                SettingsBtn.Visibility = Visibility.Visible;
            }
        }

        private async Task LoadCellColorsAsync()
        {
            try
            {
                var colors = await dbContext.CellColors
                    .ToListAsync();

                _cellColors.Clear();

                foreach (var color in colors)
                {
                    _cellColors[(color.RegeditId, color.ColumnName)] = color.ColorCode;
                    Debug.WriteLine($"Loaded: Id={color.RegeditId}, Column={color.ColumnName}, Color={color.ColorCode}");
                }

                // Обновляем ссылку в конвертере
                CellColorConverter.CellColors = _cellColors;

                Debug.WriteLine($"Total colors loaded: {_cellColors.Count}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading colors: {ex.Message}");
                _cellColors = new Dictionary<(int, string), string>();
                CellColorConverter.CellColors = _cellColors;
            }
        }

        private void InitializeCollections()
        {
            _allRegedits = new ObservableCollection<Regedit>();
            _allArchiveRegedits = new ObservableCollection<ArchiveRegedit>();
            _filteredRegedits = new ObservableCollection<Regedit>();
            _filteredArchiveRegedits = new ObservableCollection<ArchiveRegedit>();

            B2BStatuses = new ObservableCollection<B2BStatus>();
            typeOfPurchases = new ObservableCollection<TypeOfPurchase>();
            executionStatuses = new ObservableCollection<ExecutionStatus>();
        }

        private void SetupSearchDebounce()
        {
            _searchTimer = new System.Timers.Timer(500); // 500ms задержка
            _searchTimer.AutoReset = false;
            _searchTimer.Elapsed += (s, e) =>
            {
                Dispatcher.Invoke(() => PerformSearch());
            };
        }

        private void UpdateButtonVisibility(string tabHeader)
        {
            if (CurrentUser.IsAdmin)
            {
                if (tabHeader == "Основные данные")
                {
                    ArchBtn.Visibility = Visibility.Visible;
                    UnArchBtn.Visibility = Visibility.Collapsed;
                }
                else if (tabHeader == "Архив")
                {
                    ArchBtn.Visibility = Visibility.Collapsed;
                    UnArchBtn.Visibility = Visibility.Visible;
                }
                else
                {
                    ArchBtn.Visibility = Visibility.Collapsed;
                    UnArchBtn.Visibility = Visibility.Collapsed;
                }
            }
            else
            {
                ArchRegedit.Visibility = Visibility.Collapsed;
                ArchBtn.Visibility = Visibility.Collapsed;
                UnArchBtn.Visibility = Visibility.Collapsed;
            }

        }

        private void MainTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.Source is TabControl tabControl && tabControl.SelectedItem is TabItem tabItem)
            {
                string tabHeader = tabItem.Header?.ToString() ?? "";
                SetCurrentDataGrid(tabHeader);
                UpdateButtonVisibility(tabHeader);

                // Обновляем привязку DataGrid при смене вкладки
                UpdateDataGridSource();

                // Очищаем поиск при смене вкладки
                SearchTxb.Text = string.Empty;
            }
        }

        private void SetCurrentDataGrid(string tabHeader)
        {
            try
            {
                if (tabHeader == "Основные данные")
                {
                    _currentDataGrid = DGBase;
                    _isArchiveMode = false;
                    UnArchBtn.Visibility = Visibility.Collapsed;
                    ArchBtn.Visibility = Visibility.Visible;
                }
                else if (tabHeader == "Архив")
                {
                    _currentDataGrid = ArchiveDGBase;
                    _isArchiveMode = true;
                    ArchBtn.Visibility = Visibility.Collapsed;
                    UnArchBtn.Visibility = Visibility.Visible;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при переключении вкладок: {ex.Message}");
            }
        }

        private async Task LoadDataAsync()
        {
            try
            {
                ShowLoadingIndicator(true);
                await LoadCellColorsAsync();
                // Загружаем последовательно, используя один DbContext
                var regeditsList = await dbContext.Regedit
                    .Include(r => r.B2BStatus)
                    .Include(r => r.TypeOfPurchase)
                    .Include(r => r.ExecutionStatus)
                    .ToListAsync();

                var archiveRegeditsList = await dbContext.ArchiveRegedit
                    .Include(r => r.B2BStatus)
                    .Include(r => r.TypeOfPurchase)
                    .Include(r => r.ExecutionStatus)
                    .ToListAsync();

                var b2bStatusesList = await dbContext.B2BStatus.ToListAsync();
                var typeOfPurchasesList = await dbContext.TypeOfPurchase.ToListAsync();
                var executionStatusesList = await dbContext.ExecutionStatus.ToListAsync();

                //Обновляем коллекции

                _allRegedits.Clear();
                foreach (var item in regeditsList)
                    _allRegedits.Add(item);

                _allArchiveRegedits.Clear();
                foreach (var item in archiveRegeditsList)
                    _allArchiveRegedits.Add(item);

                // Применяем текущий поиск
                PerformSearch();
                B2BStatuses = new ObservableCollection<B2BStatus>(b2bStatusesList);
                typeOfPurchases = new ObservableCollection<TypeOfPurchase>(typeOfPurchasesList);
                executionStatuses = new ObservableCollection<ExecutionStatus>(executionStatusesList);
                // Привязываем справочники к комбобоксам
                B2BCombo.ItemsSource = B2BStatuses;
                TypeOfPCombo.ItemsSource = typeOfPurchases;
                ExecutionCombo.ItemsSource = executionStatuses;

                B2BComboA.ItemsSource = B2BStatuses;
                TypeOfPComboA.ItemsSource = typeOfPurchases;
                ExecutionComboA.ItemsSource = executionStatuses;
                
                Console.WriteLine($"Загружено: {_allRegedits.Count} основных, {_allArchiveRegedits.Count} архивных записей");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки данных: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                ShowLoadingIndicator(false);
            }
        }



        #region Search Logic

        private void SearchTxb_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Сбрасываем таймер при каждом вводе текста
            _searchTimer?.Stop();
            _searchTimer?.Start();
        }

        private void SearchBtn_Click(object sender, RoutedEventArgs e)
        {
            // Мгновенный поиск по кнопке
            _searchTimer?.Stop();
            PerformSearch();
        }

        private void PerformSearch()
        {
            try
            {
                string searchText = SearchTxb?.Text?.Trim().ToLower() ?? "";

                if (_isArchiveMode)
                {
                    SearchInArchive(searchText);
                }
                else
                {
                    SearchInMainData(searchText);
                }

                // Обновляем заголовок с количеством найденных записей
                UpdateSearchResultCount();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ошибка поиска: {ex.Message}");
            }
        }

        private void SearchInMainData(string searchText)
        {
            _filteredRegedits.Clear();

            var query = string.IsNullOrWhiteSpace(searchText)
                ? _allRegedits
                : new ObservableCollection<Regedit>(
                    _allRegedits.Where(item =>
                        IsMatch(item, searchText)
                    )
                );

            foreach (var item in query)
                _filteredRegedits.Add(item);

            // Обновляем источник данных для DataGrid
            if (!_isArchiveMode)
                DGBase.Items.Refresh();
        }

        private void SearchInArchive(string searchText)
        {
            _filteredArchiveRegedits.Clear();

            var query = string.IsNullOrWhiteSpace(searchText)
                ? _allArchiveRegedits
                : new ObservableCollection<ArchiveRegedit>(
                    _allArchiveRegedits.Where(item =>
                        IsArchiveMatch(item, searchText)
                    )
                );

            foreach (var item in query)
                _filteredArchiveRegedits.Add(item);

            // Обновляем источник данных для DataGrid
            if (_isArchiveMode)
                ArchiveDGBase.Items.Refresh();
        }

        private bool IsMatch(Regedit item, string searchText)
        {
            if (item == null) return false;

            return (!string.IsNullOrEmpty(item.NameLink) && item.NameLink.ToLower().Contains(searchText)) ||
                   (!string.IsNullOrEmpty(item.Customer) && item.Customer.ToLower().Contains(searchText)) ||
                   (!string.IsNullOrEmpty(item.PlaceOfDelivery) && item.PlaceOfDelivery.ToLower().Contains(searchText)) ||
                   (!string.IsNullOrEmpty(item.ReserveNumber) && item.ReserveNumber.ToLower().Contains(searchText)) ||
                   (!string.IsNullOrEmpty(item.NationalMode) && item.NationalMode.ToLower().Contains(searchText)) ||
                   (!string.IsNullOrEmpty(item.Winner) && item.Winner.ToLower().Contains(searchText)) ||
                   (!string.IsNullOrEmpty(item.Description) && item.Description.ToLower().Contains(searchText)) ||
                   (!string.IsNullOrEmpty(item.Note) && item.Note.ToLower().Contains(searchText)) ||
                   (item.B2BStatus != null && !string.IsNullOrEmpty(item.B2BStatus.NameB2B) &&
                       item.B2BStatus.NameB2B.ToLower().Contains(searchText)) ||
                   (item.TypeOfPurchase != null && !string.IsNullOrEmpty(item.TypeOfPurchase.NameOfPurchase) &&
                       item.TypeOfPurchase.NameOfPurchase.ToLower().Contains(searchText)) ||
                   (item.ExecutionStatus != null && !string.IsNullOrEmpty(item.ExecutionStatus.NameExecution) &&
                       item.ExecutionStatus.NameExecution.ToLower().Contains(searchText)) ||
                   (item.NMCK.ToString().Contains(searchText)) ||
                   (item.MinPrice.ToString().Contains(searchText)) ||
                   (item.ResultPrice.ToString().Contains(searchText)) ||
                   (item.DateOfTransferForPlacement?.ToString("dd.MM.yyyy").Contains(searchText) ?? false) ||
                   (item.DateOfPlacement?.ToString("dd.MM.yyyy").Contains(searchText) ?? false) ||
                   (item.BiddingDate?.ToString("dd.MM.yyyy").Contains(searchText) ?? false) ||
                   (item.DateResults?.ToString("dd.MM.yyyy").Contains(searchText) ?? false) ||
                   (item.DateOfConclusionOfTheContract?.ToString("dd.MM.yyyy").Contains(searchText) ?? false);
        }

        private bool IsArchiveMatch(ArchiveRegedit item, string searchText)
        {
            if (item == null) return false;

            return (!string.IsNullOrEmpty(item.NameLink) && item.NameLink.ToLower().Contains(searchText)) ||
                   (!string.IsNullOrEmpty(item.Customer) && item.Customer.ToLower().Contains(searchText)) ||
                   (!string.IsNullOrEmpty(item.PlaceOfDelivery) && item.PlaceOfDelivery.ToLower().Contains(searchText)) ||
                   (!string.IsNullOrEmpty(item.ReserveNumber) && item.ReserveNumber.ToLower().Contains(searchText)) ||
                   (!string.IsNullOrEmpty(item.NationalMode) && item.NationalMode.ToLower().Contains(searchText)) ||
                   (!string.IsNullOrEmpty(item.Winner) && item.Winner.ToLower().Contains(searchText)) ||
                   (!string.IsNullOrEmpty(item.Description) && item.Description.ToLower().Contains(searchText)) ||
                   (!string.IsNullOrEmpty(item.Note) && item.Note.ToLower().Contains(searchText)) ||
                   (item.B2BStatus != null && !string.IsNullOrEmpty(item.B2BStatus.NameB2B) &&
                       item.B2BStatus.NameB2B.ToLower().Contains(searchText)) ||
                   (item.TypeOfPurchase != null && !string.IsNullOrEmpty(item.TypeOfPurchase.NameOfPurchase) &&
                       item.TypeOfPurchase.NameOfPurchase.ToLower().Contains(searchText)) ||
                   (item.ExecutionStatus != null && !string.IsNullOrEmpty(item.ExecutionStatus.NameExecution) &&
                       item.ExecutionStatus.NameExecution.ToLower().Contains(searchText)) ||
                   (item.NMCK.ToString().Contains(searchText)) ||
                   (item.MinPrice.ToString().Contains(searchText)) ||
                   (item.ResultPrice.ToString().Contains(searchText)) ||
                   (item.ArchivateDate?.ToString("dd.MM.yyyy").Contains(searchText) ?? false) ||
                   (item.DateOfTransferForPlacement?.ToString("dd.MM.yyyy").Contains(searchText) ?? false) ||
                   (item.DateOfPlacement?.ToString("dd.MM.yyyy").Contains(searchText) ?? false) ||
                   (item.BiddingDate?.ToString("dd.MM.yyyy").Contains(searchText) ?? false) ||
                   (item.DateResults?.ToString("dd.MM.yyyy").Contains(searchText) ?? false) ||
                   (item.DateOfConclusionOfTheContract?.ToString("dd.MM.yyyy").Contains(searchText) ?? false);
        }

        private void UpdateSearchResultCount()
        {
            int count = _isArchiveMode ? _filteredArchiveRegedits.Count : _filteredRegedits.Count;
            int total = _isArchiveMode ? _allArchiveRegedits.Count : _allRegedits.Count;

            if (!string.IsNullOrWhiteSpace(SearchTxb.Text))
            {
                SearchHintText.Text = $"Найдено: {count} из {total}";
                SearchHintText.Visibility = Visibility.Visible;
            }
            else
            {
                SearchHintText.Visibility = Visibility.Collapsed;
            }
        }

        private void ClearSearch_Click(object sender, RoutedEventArgs e)
        {
            SearchTxb.Text = string.Empty;
            PerformSearch();
        }

        #endregion

        #region CRUD Operations with Filtered Collections

        private async void DeleteItem_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Проверяем, какой DataGrid активен
                if (MainTabControl.SelectedItem is TabItem tabItem)
                {
                    string tabHeader = tabItem.Header?.ToString() ?? "";

                    if (tabHeader == "Основные данные")
                    {
                        await DeleteFromMainData();
                    }
                    else if (tabHeader == "Архив")
                    {
                        await DeleteFromArchive();
                    }
                    else
                    {
                        MessageBox.Show("Неизвестная вкладка", "Ошибка",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при удалении: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task DeleteFromMainData()
        {
            var selectedItem = DGBase.SelectedItem as Regedit;
            if (selectedItem == null)
            {
                MessageBox.Show("Выберите запись для удаления из основных данных!", "Информация",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show($"Вы уверены, что хотите удалить запись \"{selectedItem.NameLink}\"?",
                "Подтверждение удаления", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    // Удаляем из всех коллекций
                    _allRegedits.Remove(selectedItem);
                    _filteredRegedits.Remove(selectedItem);

                    // Удаляем из базы данных
                    dbContext.Regedit.Remove(selectedItem);
                    await dbContext.SaveChangesAsync();

                    // Обновляем отображение
                    DGBase.Items.Refresh();

                    // Обновляем счетчик поиска
                    UpdateSearchResultCount();

                    MessageBox.Show("Запись успешно удалена.", "Успех",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка при удалении: {ex.Message}", "Ошибка",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async Task DeleteFromArchive()
        {
            var selectedItem = ArchiveDGBase.SelectedItem as ArchiveRegedit;
            if (selectedItem == null)
            {
                MessageBox.Show("Выберите запись для удаления из архива!", "Информация",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show($"Вы уверены, что хотите удалить архивную запись \"{selectedItem.NameLink}\"?",
                "Подтверждение удаления", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    // Удаляем из всех коллекций
                    _allArchiveRegedits.Remove(selectedItem);
                    _filteredArchiveRegedits.Remove(selectedItem);

                    // Удаляем из базы данных
                    dbContext.ArchiveRegedit.Remove(selectedItem);
                    await dbContext.SaveChangesAsync();

                    // Обновляем отображение
                    ArchiveDGBase.Items.Refresh();

                    // Обновляем счетчик поиска
                    UpdateSearchResultCount();

                    MessageBox.Show("Архивная запись успешно удалена.", "Успех",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка при удалении: {ex.Message}", "Ошибка",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async void ArchiveButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (MainTabControl.SelectedItem is TabItem tabItem && tabItem.Header?.ToString() != "Основные данные")
                {
                    MessageBox.Show("Для архивации вы должны находиться на вкладке 'Основные данные'", "Ошибка",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var selectedItem = DGBase.SelectedItem as Regedit;
                if (selectedItem == null)
                {
                    MessageBox.Show("Выберите запись для архивации!", "Ошибка");
                    return;
                }

                var result = MessageBox.Show($"Вы уверены, что хотите архивировать запись \"{selectedItem.NameLink}\"?",
                    "Подтверждение архивации", MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    var archivedItem = new ArchiveRegedit
                    {
                        IdOld = selectedItem.Id,
                        NameLink = selectedItem.NameLink,
                        PlaceOfDelivery = selectedItem.PlaceOfDelivery,
                        ReserveNumber = selectedItem.ReserveNumber,
                        NationalMode = selectedItem.NationalMode,
                        DateOfTransferForPlacement = selectedItem.DateOfTransferForPlacement,
                        DateOfPlacement = selectedItem.DateOfPlacement,
                        BiddingDate = selectedItem.BiddingDate,
                        DateResults = selectedItem.DateResults,
                        NMCK = selectedItem.NMCK,
                        MinPrice = selectedItem.MinPrice,
                        ResultPrice = selectedItem.ResultPrice,
                        Winner = selectedItem.Winner,
                        DateOfConclusionOfTheContract = selectedItem.DateOfConclusionOfTheContract,
                        DeliveryTime = selectedItem.DeliveryTime,
                        Description = selectedItem.Description,
                        Note = selectedItem.Note,
                        Customer = selectedItem.Customer,
                        TypeOfPurchaseId = selectedItem.TypeOfPurchaseId,
                        B2BStatusId = selectedItem.B2BStatusId,
                        ExecutionStatusId = selectedItem.ExecutionStatusId,
                        ArchivateDate = DateTime.Now
                    };

                    // Загружаем навигационные свойства для архивной записи
                    archivedItem.B2BStatus = selectedItem.B2BStatus;
                    archivedItem.TypeOfPurchase = selectedItem.TypeOfPurchase;
                    archivedItem.ExecutionStatus = selectedItem.ExecutionStatus;

                    // Добавляем в архивные коллекции
                    _allArchiveRegedits.Add(archivedItem);

                    // Если мы на вкладке архива и есть активный поиск, проверяем подходит ли запись под фильтр
                    if (_isArchiveMode && !string.IsNullOrWhiteSpace(SearchTxb.Text))
                    {
                        if (IsArchiveMatch(archivedItem, SearchTxb.Text.ToLower()))
                            _filteredArchiveRegedits.Add(archivedItem);
                    }

                    dbContext.ArchiveRegedit.Add(archivedItem);

                    // Удаляем из основных коллекций
                    _allRegedits.Remove(selectedItem);
                    _filteredRegedits.Remove(selectedItem);
                    dbContext.Regedit.Remove(selectedItem);

                    // Сохраняем изменения
                    await dbContext.SaveChangesAsync();

                    // Обновляем отображение
                    DGBase.Items.Refresh();
                    ArchiveDGBase.Items.Refresh();

                    UpdateSearchResultCount();

                    MessageBox.Show("Запись успешно архивирована.", "Успех",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при архивации: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void UnarchiveButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (MainTabControl.SelectedItem is TabItem tabItem && tabItem.Header?.ToString() != "Архив")
                {
                    MessageBox.Show("Для разархивации вы должны находиться на вкладке 'Архив'", "Ошибка",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var selectedArchive = ArchiveDGBase.SelectedItem as ArchiveRegedit;
                if (selectedArchive == null)
                {
                    MessageBox.Show("Выберите запись для разархивации!", "Ошибка");
                    return;
                }

                var result = MessageBox.Show($"Вы уверены, что хотите разархивировать запись \"{selectedArchive.NameLink}\"?",
                    "Подтверждение разархивации", MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    var restoredItem = new Regedit
                    {
                        NameLink = selectedArchive.NameLink,
                        PlaceOfDelivery = selectedArchive.PlaceOfDelivery,
                        ReserveNumber = selectedArchive.ReserveNumber,
                        NationalMode = selectedArchive.NationalMode,
                        DateOfTransferForPlacement = selectedArchive.DateOfTransferForPlacement,
                        DateOfPlacement = selectedArchive.DateOfPlacement,
                        BiddingDate = selectedArchive.BiddingDate,
                        DateResults = selectedArchive.DateResults,
                        NMCK = selectedArchive.NMCK,
                        MinPrice = selectedArchive.MinPrice,
                        ResultPrice = selectedArchive.ResultPrice,
                        Winner = selectedArchive.Winner,
                        DateOfConclusionOfTheContract = selectedArchive.DateOfConclusionOfTheContract,
                        DeliveryTime = selectedArchive.DeliveryTime,
                        Description = selectedArchive.Description,
                        Note = selectedArchive.Note,
                        Customer = selectedArchive.Customer,
                        TypeOfPurchaseId = selectedArchive.TypeOfPurchaseId,
                        B2BStatusId = selectedArchive.B2BStatusId,
                        ExecutionStatusId = selectedArchive.ExecutionStatusId,
                        IsFinished = false
                    };

                    // Загружаем навигационные свойства
                    restoredItem.B2BStatus = selectedArchive.B2BStatus;
                    restoredItem.TypeOfPurchase = selectedArchive.TypeOfPurchase;
                    restoredItem.ExecutionStatus = selectedArchive.ExecutionStatus;

                    // Добавляем в основные коллекции
                    _allRegedits.Add(restoredItem);

                    // Если мы на вкладке основных данных и есть активный поиск, проверяем подходит ли запись под фильтр
                    if (!_isArchiveMode && !string.IsNullOrWhiteSpace(SearchTxb.Text))
                    {
                        if (IsMatch(restoredItem, SearchTxb.Text.ToLower()))
                            _filteredRegedits.Add(restoredItem);
                    }

                    dbContext.Regedit.Add(restoredItem);

                    // Удаляем из архивных коллекций
                    _allArchiveRegedits.Remove(selectedArchive);
                    _filteredArchiveRegedits.Remove(selectedArchive);
                    dbContext.ArchiveRegedit.Remove(selectedArchive);

                    // Сохраняем изменения
                    await dbContext.SaveChangesAsync();

                    // Обновляем отображение
                    DGBase.Items.Refresh();
                    ArchiveDGBase.Items.Refresh();

                    UpdateSearchResultCount();

                    MessageBox.Show("Запись успешно разархивирована.", "Успех",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при разархивации: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void AddItem_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Создаем новую запись с значениями по умолчанию
                var newProduct = new Regedit
                {
                    NameLink = "Новая запись",
                    PlaceOfDelivery = "Место поставки",
                    ReserveNumber = "Номер резерва",
                    NationalMode = "Нац режим",
                    DateOfTransferForPlacement = DateTime.Now,
                    DateOfPlacement = DateTime.Now,
                    BiddingDate = DateTime.Now,
                    DateResults = DateTime.Now,
                    NMCK = 0,
                    MinPrice = 0,
                    ResultPrice = 0,
                    Winner = "Победитель",
                    DateOfConclusionOfTheContract = DateTime.Now,
                    DeliveryTime = "30 дней",
                    Description = "Описание",
                    Note = "Примечание",
                    Customer = "Заказчик",
                    TypeOfPurchaseId = typeOfPurchases.FirstOrDefault()?.ID ?? 1,
                    B2BStatusId = B2BStatuses.FirstOrDefault()?.ID ?? 1,
                    ExecutionStatusId = executionStatuses.FirstOrDefault()?.ID ?? 1,
                    IsFinished = false
                };

                // Загружаем навигационные свойства из справочников
                newProduct.TypeOfPurchase = typeOfPurchases.FirstOrDefault(t => t.ID == newProduct.TypeOfPurchaseId);
                newProduct.B2BStatus = B2BStatuses.FirstOrDefault(b => b.ID == newProduct.B2BStatusId);
                newProduct.ExecutionStatus = executionStatuses.FirstOrDefault(e => e.ID == newProduct.ExecutionStatusId);

                // Добавляем в общую коллекцию
                _allRegedits.Add(newProduct);

                // Добавляем в отфильтрованную коллекцию, если подходит под текущий поиск
                bool addToFiltered = !_isArchiveMode; // Только для вкладки основных данных

                if (addToFiltered)
                {
                    if (string.IsNullOrWhiteSpace(SearchTxb.Text) || IsMatch(newProduct, SearchTxb.Text.ToLower()))
                    {
                        _filteredRegedits.Add(newProduct);
                    }
                }

                // Добавляем в БД
                dbContext.Regedit.Add(newProduct);
                await dbContext.SaveChangesAsync();

                // Обновляем отображение
                DGBase.Items.Refresh();

                // Прокручиваем к новой записи и выделяем её
                DGBase.ScrollIntoView(newProduct);
                DGBase.SelectedItem = newProduct;

                UpdateSearchResultCount();

                MessageBox.Show("Новая запись успешно добавлена.", "Успех",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при добавлении записи: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            await LoadDataAsync();
            await LoadCellColorsAsync();
            MessageBox.Show("Данные обновлены.", "Информация",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async void ProductDataGrid_RowEditEnding(object sender, DataGridRowEditEndingEventArgs e)
        {
            // Откладываем обработку, чтобы дать время завершить редактирование
            if (e.EditAction != DataGridEditAction.Commit)
                return;

            // Используем Dispatcher для отложенного выполнения после завершения редактирования
            Dispatcher.BeginInvoke(new Action(async () =>
            {
                await SaveEditedItem(e.Row.Item);
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        private async Task SaveEditedItem(object item)
        {
            try
            {
                if (item is Regedit editedProduct)
                {
                    // Нормализуем связи
                    editedProduct.B2BStatusId = editedProduct.B2BStatus?.ID ?? 0;
                    editedProduct.TypeOfPurchaseId = editedProduct.TypeOfPurchase?.ID ?? 0;
                    editedProduct.ExecutionStatusId = editedProduct.ExecutionStatus?.ID ?? 0;

                    // Сохраняем в БД
                    if (editedProduct.Id == 0)
                    {
                        dbContext.Regedit.Add(editedProduct);
                    }
                    else
                    {
                        // Проверяем, отслеживается ли уже эта сущность
                        var local = dbContext.Regedit.Local.FirstOrDefault(r => r.Id == editedProduct.Id);
                        if (local != null)
                        {
                            dbContext.Entry(local).State = EntityState.Detached;
                        }
                        dbContext.Regedit.Update(editedProduct);
                    }

                    await dbContext.SaveChangesAsync();

                    // Обновляем отображение
                    DGBase.Items.Refresh();

                    // Не показываем сообщение при каждом сохранении, чтобы не раздражать пользователя
                    // Можно показывать только при ошибках
                }
            }
            catch (DbUpdateException dbEx)
            {
                MessageBox.Show(
                    $"Ошибка сохранения в базу данных:\n{dbEx.InnerException?.Message ?? dbEx.Message}",
                    "Ошибка БД",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Ошибка при сохранении:\n{ex.Message}",
                    "Ошибка",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        #endregion

        #region Helper Methods

        private void ShowLoadingIndicator(bool show)
        {
            // Если у вас есть индикатор загрузки в XAML
            if (LoadingIndicator != null)
            {
                LoadingIndicator.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            }

            // Блокируем кнопки во время загрузки
            ArchBtn.IsEnabled = !show;
            UnArchBtn.IsEnabled = !show;
            SearchBtn.IsEnabled = !show;
            AddItemButton.IsEnabled = !show; // Убедитесь, что у кнопки есть имя x:Name="AddItemButton"
            DeleteButton.IsEnabled = !show;   // Убедитесь, что у кнопки есть имя x:Name="DeleteButton"
            RefreshButton.IsEnabled = !show;  // Убедитесь, что у кнопки есть имя x:Name="RefreshButton"
        }

        private void UpdateDataGridSource()
        {
            if (_isArchiveMode)
            {
                ArchiveDGBase.ItemsSource = _filteredArchiveRegedits;
            }
            else
            {
                DGBase.ItemsSource = _filteredRegedits;
            }
        }

        // Обновление навигационных свойств при изменении ID
        private void UpdateNavigationProperties(Regedit regedit)
        {
            if (regedit.B2BStatusId > 0)
                regedit.B2BStatus = B2BStatuses.FirstOrDefault(b => b.ID == regedit.B2BStatusId);

            if (regedit.TypeOfPurchaseId > 0)
                regedit.TypeOfPurchase = typeOfPurchases.FirstOrDefault(t => t.ID == regedit.TypeOfPurchaseId);

            if (regedit.ExecutionStatusId > 0)
                regedit.ExecutionStatus = executionStatuses.FirstOrDefault(e => e.ID == regedit.ExecutionStatusId);
        }

        private void UpdateArchiveNavigationProperties(ArchiveRegedit archive)
        {
            if (archive.B2BStatusId > 0)
                archive.B2BStatus = B2BStatuses.FirstOrDefault(b => b.ID == archive.B2BStatusId);

            if (archive.TypeOfPurchaseId > 0)
                archive.TypeOfPurchase = typeOfPurchases.FirstOrDefault(t => t.ID == archive.TypeOfPurchaseId);

            if (archive.ExecutionStatusId > 0)
                archive.ExecutionStatus = executionStatuses.FirstOrDefault(e => e.ID == archive.ExecutionStatusId);
        }

        #endregion

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
                e.Handled = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка открытия ссылки: {ex.Message}");
            }
        }

        

        private void InfoBtn_Click(object sender, RoutedEventArgs e)
        {
            if (UserInfo.Visibility == Visibility.Collapsed)
            {
                UserInfo.Visibility = Visibility.Visible;
            }
            else
            {
                UserInfo.Visibility = Visibility.Collapsed;
            }

        }
        private void SettingsBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                NavigationService.Navigate(new AdminPage());
            }
            catch(Exception ex) 
            {
                MessageBox.Show(ex.Message);
            }
            
        }

        /// <summary>
        /// Получает имя свойства из колонки DataGrid
        /// </summary>
        private string GetColumnName(DataGridColumn column)
        {
            // Для текстовых колонок
            if (column is DataGridTextColumn textColumn && textColumn.Binding is Binding binding)
                return binding.Path.Path;

            // Для колонок-комбобоксов
            if (column is DataGridComboBoxColumn comboColumn && comboColumn.SelectedItemBinding is Binding selectedBinding)
                return selectedBinding.Path.Path;

            // Для колонок-чекбоксов
            if (column is DataGridCheckBoxColumn checkColumn && checkColumn.Binding is Binding checkBinding)
                return checkBinding.Path.Path;

            // Для шаблонных колонок используем Header
            return column.Header?.ToString();
        }

        
        

        private void DataGrid_SelectedCellsChanged(object sender, SelectedCellsChangedEventArgs e)
        {
            var grid = sender as DataGrid;
            if (grid?.CurrentCell != null && grid.CurrentCell.Item != null)
            {
                _selectedRowItem = grid.CurrentCell.Item;
                _selectedColumnName = GetColumnName(grid.CurrentCell.Column);
            }
        }
        private async void ApplyColorButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedRowItem == null || string.IsNullOrEmpty(_selectedColumnName))
            {
                MessageBox.Show("Выберите ячейку для покраски", "Информация");
                return;
            }

            if (ColorComboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                string selectedColor = selectedItem.Tag.ToString();

                int recordId = 0;
                if (_selectedRowItem is Regedit reg)
                    recordId = reg.Id;
                else if (_selectedRowItem is ArchiveRegedit arch)
                    recordId = arch.IdOld;
                else
                    return;

                try
                {
                    // Сохраняем в БД
                    var existing = await dbContext.CellColors
                        .FirstOrDefaultAsync(c => c.RegeditId == recordId
                                               && c.ColumnName == _selectedColumnName);
         

                    if (existing != null)
                    {
                        existing.ColorCode = selectedColor;
                        dbContext.CellColors.Update(existing);
                    }
                    else
                    {
                        var newColor = new CellColors
                        {
                            RegeditId = recordId,
                            ColumnName = _selectedColumnName,
                            ColorCode = selectedColor,
                        };
                        dbContext.CellColors.Add(newColor);
                    }

                    await dbContext.SaveChangesAsync();

                    // Обновляем словарь
                    _cellColors[(recordId, _selectedColumnName)] = selectedColor;

                    // Обновляем конвертер
                    CellColorConverter.CellColors = _cellColors;

                    // Принудительно обновляем отображение
                    RefreshCurrentDataGrid();

                    MessageBox.Show("Цвет применен", "Успех");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка: {ex.Message}");
                }
            }
        }
        private void RefreshCurrentDataGrid()
        {
            if (_isArchiveMode)
            {
                ArchiveDGBase.Items.Refresh();
            }
            else
            {
                DGBase.Items.Refresh();
            }
        }

        private void ClearColorButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedRowItem == null || string.IsNullOrEmpty(_selectedColumnName))
            {
                MessageBox.Show("Выберите ячейку для сброса цвета", "Информация",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            int recordId = 0;
            if (_selectedRowItem is Regedit reg)
                recordId = reg.Id;
            else if (_selectedRowItem is ArchiveRegedit arch)
                recordId = arch.IdOld;
            else
                return;

            try
            {
                var existing = dbContext.CellColors
                    .FirstOrDefault(c => c.RegeditId == recordId
                                       && c.ColumnName == _selectedColumnName);

                if (existing != null)
                {
                    dbContext.CellColors.Remove(existing);
                    dbContext.SaveChanges();

                    // Удаляем из словаря
                    _cellColors.Remove((recordId, _selectedColumnName));

                    // Перекрашиваем строку
                    RefreshDataGridColors(_isArchiveMode);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка сброса цвета: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void RefreshDataGridColors(bool isArchive)
        {
            var grid = isArchive ? ArchiveDGBase : DGBase;
            // Простейший способ – перезагрузить визуальное представление
            grid.Items.Refresh();
            // Можно также обновить только видимые строки, но Refresh проще
        }
        
    }
}
