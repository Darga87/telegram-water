-- Обновляем URL изображений на реальные фотографии из Telegram
UPDATE Products 
SET ImageUrl = 'https://www.kasandbox.org/programming-images/avatars/leaf-blue.png'
WHERE Name = 'Вода 19л';

UPDATE Products 
SET ImageUrl = 'https://www.kasandbox.org/programming-images/avatars/leaf-green.png'
WHERE Name = 'Вода 5л';

UPDATE Products 
SET ImageUrl = 'https://www.kasandbox.org/programming-images/avatars/leaf-grey.png'
WHERE Name = 'Вода 0.5л';